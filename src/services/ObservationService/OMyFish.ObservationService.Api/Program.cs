using System.Text;
using DbUp;
using MassTransit;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Minio;
using OMyFish.ObservationService.Api.Endpoints;
using OMyFish.ObservationService.Application.Commands;
using OMyFish.ObservationService.Application.Interfaces;
using OMyFish.ObservationService.Infrastructure.Messaging;
using OMyFish.ObservationService.Infrastructure.Persistence;
using OMyFish.ObservationService.Infrastructure.Repositories;
using OMyFish.ObservationService.Infrastructure.Storage;
using OMyFish.Shared.BuildingBlocks.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] observations | {Message:lj}{NewLine}{Exception}"));

// Database (PostGIS)
builder.Services.AddDbContext<ObservationDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Repositories
builder.Services.AddScoped<IObservationRepository, ObservationRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// CQRS
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(CreateObservationCommand).Assembly));

// MinIO
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
    // Transactional outbox — the observation save and the event publish now commit in one
    // DB transaction, so a crash between the two can no longer drop the event
    // (BACKLOG.md item F, WEAKNESS_AUDIT.md §2.3).
    x.AddEntityFrameworkOutbox<ObservationDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    x.UsingRabbitMq((ctx, cfg) =>
    {
        var host = builder.Configuration["RabbitMQ__Host"]
                ?? builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq";
        var port = ushort.Parse(builder.Configuration["RabbitMQ__Port"]
                ?? builder.Configuration["RabbitMQ:Port"] ?? "5672");
        cfg.Host(host, port, "/", h =>
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
        // Refresh tokens are signed with the same key but must never
        // authenticate API calls — only /api/v1/auth/refresh accepts them.
        opts.Events = new JwtBearerEvents
        {
            OnTokenValidated = ctx =>
            {
                if (ctx.Principal?.FindFirst("token_type")?.Value != "access")
                    ctx.Fail("Not an access token.");
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddOpenApi();

// Catches unhandled exceptions so callers get a clean 5xx instead of a raw exception page
// (BACKLOG.md item F, WEAKNESS_AUDIT.md §2.2).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("observation-service"))
        .AddAspNetCoreInstrumentation(opts => opts.RecordException = true)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opts => opts.Endpoint = new Uri(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://jaeger:4317")));

var app = builder.Build();

// Applies migrations/ObservationService/*.sql (embedded at build time) via DbUp instead of
// EF's EnsureCreated, which only creates schema on an empty DB and silently no-ops
// otherwise — that gap caused a real production-shaped incident on another service in this
// repo (a new required column went live with no matching schema change). Fails fast on
// migration failure rather than starting against a schema the app doesn't match
// (BACKLOG.md item F, WEAKNESS_AUDIT.md §3.1).
var migrator = DeployChanges.To
    .PostgresqlDatabase(builder.Configuration.GetConnectionString("Default"))
    .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly, s => s.StartsWith("Migrations.") && s.EndsWith(".sql"))
    .JournalToPostgresqlTable("public", "schemaversions_observation")
    .LogToConsole()
    .Build();
var migrationResult = migrator.PerformUpgrade();
if (!migrationResult.Successful)
    throw new InvalidOperationException("Database migration failed — see inner exception.", migrationResult.Error);

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseHttpMetrics();

app.MapGet("/health", () => "ok");
app.MapMetrics("/metrics");
app.MapOpenApi();
app.MapScalarApiReference();
app.MapObservationEndpoints();

app.Run();

// Exposes the top-level-statements Program class to WebApplicationFactory<Program> in
// OMyFish.ObservationService.Tests — top-level statements otherwise generate it `internal`,
// which isn't visible across assemblies.
public partial class Program;

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
