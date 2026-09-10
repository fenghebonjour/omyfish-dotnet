using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Minio;
using OMyFish.SpeciesService.Application.Commands;
using OMyFish.SpeciesService.Application.Interfaces;
using OMyFish.SpeciesService.Api.Endpoints;
using OMyFish.SpeciesService.Domain.Entities;
using OMyFish.SpeciesService.Infrastructure.ExternalServices;
using OMyFish.SpeciesService.Infrastructure.Messaging;
using OMyFish.SpeciesService.Infrastructure.Persistence;
using OMyFish.SpeciesService.Infrastructure.Repositories;
using OMyFish.SpeciesService.Infrastructure.Storage;
using OMyFish.Shared.BuildingBlocks.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] species | {Message:lj}{NewLine}{Exception}"));

// Database
builder.Services.AddDbContext<SpeciesDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Repositories
builder.Services.AddScoped<ISpeciesRepository, SpeciesRepository>();

// CQRS
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(IdentifyFishCommand).Assembly));

// AI service HTTP client
// Bounded timeout so a slow (not down) ai-service fails fast instead of hanging on the
// BCL default of 100s with no global handler to catch the resulting TaskCanceledException
// (BACKLOG.md item F, WEAKNESS_AUDIT.md §2.1).
builder.Services.AddHttpClient<IAIServiceClient, AIServiceClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["AIService__BaseUrl"]
        ?? builder.Configuration["AIService:BaseUrl"]
        ?? "http://ai-service:8000");
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Object storage (identify persists the image and returns a real storage key)
var minioEndpoint = builder.Configuration["MinIO__Endpoint"]
                 ?? builder.Configuration["MinIO:Endpoint"]
                 ?? "minio:9000";
var minioAccess = builder.Configuration["MinIO__AccessKey"]
               ?? builder.Configuration["MinIO:AccessKey"] ?? "minioadmin";
var minioSecret = builder.Configuration["MinIO__SecretKey"]
               ?? builder.Configuration["MinIO:SecretKey"] ?? "minioadmin";

builder.Services.AddSingleton<IMinioClient>(_ =>
    new MinioClient()
        .WithEndpoint(minioEndpoint)
        .WithCredentials(minioAccess, minioSecret)
        .Build());
builder.Services.AddScoped<IStorageService, MinIOStorageService>();

// Messaging
builder.Services.AddScoped<IMessagePublisher, RabbitMQPublisher>();

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((ctx, cfg) =>
    {
        var host = builder.Configuration["RabbitMQ__Host"]
                ?? builder.Configuration["RabbitMQ:Host"]
                ?? "rabbitmq";
        cfg.Host(host, "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ__Username"]
                    ?? builder.Configuration["RabbitMQ:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ__Password"]
                    ?? builder.Configuration["RabbitMQ:Password"] ?? "guest");
        });
        cfg.ConfigureEndpoints(ctx);
    });
});

// JWT auth
var jwtSecret = builder.Configuration["Jwt__Secret"] ?? builder.Configuration["Jwt:Secret"];
if (builder.Environment.IsProduction() && (jwtSecret is null || jwtSecret.StartsWith("dev-secret")))
    throw new InvalidOperationException("Jwt__Secret must be set to a non-default value in production.");
jwtSecret ??= "dev-secret-change-in-production-min-32-chars";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddOpenApi();

// Catches unhandled exceptions (e.g. AIServiceClient's TaskCanceledException on a slow
// ai-service) so callers get a clean 5xx instead of a raw exception page (BACKLOG.md item F,
// WEAKNESS_AUDIT.md §2.2).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// /identify and /bite-score/* are anonymous and drive cost on the external AI/weather
// service — without a limit here they were open to unbounded free usage
// (BACKLOG.md item F, WEAKNESS_AUDIT.md §1.2). Partitioned per client IP since there's no
// authenticated user on these routes.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("identify", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    options.AddPolicy("bite-score", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("species-service"))
        .AddAspNetCoreInstrumentation(opts => opts.RecordException = true)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opts => opts.Endpoint = new Uri(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://jaeger:4317")));

var app = builder.Build();

// Ensure schema exists (fault-tolerant for cold starts)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<SpeciesDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
    catch { /* DB may not be ready yet; migrations handle schema */ }
}

// Seed species from fish_info.json if available
var metadataPath = app.Configuration["Seeding__MetadataPath"] ?? app.Configuration["Seeding:MetadataPath"];
if (!string.IsNullOrEmpty(metadataPath) && File.Exists(metadataPath))
{
    using var scope = app.Services.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<ISpeciesRepository>();
    try
    {
        var json = await File.ReadAllTextAsync(metadataPath);
        var entries = JsonSerializer.Deserialize<JsonElement[]>(json);
        if (entries is not null)
        {
            foreach (var entry in entries)
            {
                var scientificName = entry.TryGetProperty("scientific_name", out var sn) ? sn.GetString() : null;
                var commonName = entry.TryGetProperty("species", out var sp) ? sp.GetString()?.Replace("_", " ") : null;
                var habitat = entry.TryGetProperty("habitat", out var h) ? h.GetString() : "";
                var status = entry.TryGetProperty("conservation_status", out var cs) ? cs.GetString() : "Unknown";
                var description = entry.TryGetProperty("description", out var d) ? d.GetString() : "";

                if (string.IsNullOrEmpty(scientificName) || string.IsNullOrEmpty(commonName)) continue;

                var species = Species.Create(
                    scientificName,
                    commonName,
                    "Unknown",
                    status ?? "Unknown",
                    habitat ?? "",
                    habitat ?? "",
                    description ?? "",
                    false);

                await repo.AddIfNotExistsAsync(species);
            }
            app.Logger.LogInformation("Species seeding complete from {Path}", metadataPath);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Species seeding failed — continuing without seed data");
    }
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseHttpMetrics();

app.MapGet("/health", () => "ok");
app.MapMetrics("/metrics");
app.MapOpenApi();
app.MapScalarApiReference();
app.MapSpeciesEndpoints();
app.MapIdentificationEndpoints();
app.MapBiteScoreEndpoints();
app.MapRegsEndpoints();

app.Run();

// Logs and turns any exception the endpoints/middleware don't already handle into a clean
// JSON 5xx response, instead of ASP.NET Core's default unhandled-exception behavior
// (BACKLOG.md item F, WEAKNESS_AUDIT.md §2.2).
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception, "Unhandled exception on {Path}", httpContext.Request.Path);

        httpContext.Response.StatusCode = exception is TaskCanceledException or TimeoutException
            ? StatusCodes.Status504GatewayTimeout
            : StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred." }, ct);
        return true;
    }
}
