using System.Text;
using DbUp;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Minio;
using OMyFish.ShopService.Api;
using OMyFish.ShopService.Api.Endpoints;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Infrastructure.Payments;
using OMyFish.ShopService.Infrastructure.Persistence;
using OMyFish.ShopService.Infrastructure.Repositories;
using OMyFish.ShopService.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] shop | {Message:lj}{NewLine}{Exception}"));

// Database
builder.Services.AddDbContext<ShopDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Repositories
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ICartRepository, CartRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// CQRS
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(OMyFish.ShopService.Application.Commands.CheckoutCommand).Assembly));

// MinIO
var minioEndpoint = builder.Configuration["MinIO__Endpoint"] ?? builder.Configuration["MinIO:Endpoint"] ?? "minio:9000";
var minioAccess = builder.Configuration["MinIO__AccessKey"] ?? builder.Configuration["MinIO:AccessKey"] ?? "minioadmin";
var minioSecret = builder.Configuration["MinIO__SecretKey"] ?? builder.Configuration["MinIO:SecretKey"] ?? "minioadmin";

builder.Services.AddSingleton<IMinioClient>(_ =>
    new MinioClient()
        .WithEndpoint(minioEndpoint)
        .WithCredentials(minioAccess, minioSecret)
        .Build());
builder.Services.AddScoped<IImageStorageService, MinIOImageStorageService>();

// Stripe — checkout returns 503 until this is configured. Can reuse the same Stripe account's
// secret key as IdentityService's billing module, but the webhook secret must be its own —
// Omyshop's checkout-session webhook is a separate Stripe endpoint from the billing-subscription
// one.
builder.Services.AddSingleton(sp => new StripeCheckoutGateway(
    secretKey: builder.Configuration["Stripe__SecretKey"] ?? "",
    webhookSecret: builder.Configuration["Stripe__ShopWebhookSecret"] ?? "",
    currency: builder.Configuration["Stripe__ShopCurrency"] ?? "cad",
    logger: sp.GetRequiredService<ILogger<StripeCheckoutGateway>>()));
builder.Services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<StripeCheckoutGateway>());

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(opts =>
{
    opts.MultipartBodyLengthLimit = 20 * 1024 * 1024; // 20MB, matching the upload size cap
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
        // Refresh tokens are signed with the same key but must never authenticate API calls.
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

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("shop-service"))
        .AddAspNetCoreInstrumentation(opts => opts.RecordException = true)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opts => opts.Endpoint = new Uri(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://jaeger:4317")));

var app = builder.Build();

// Applies migrations/ShopService/*.sql (embedded at build time) via DbUp before app.Run(),
// failing fast on migration failure rather than starting against a schema the app doesn't match.
var migrator = DeployChanges.To
    .PostgresqlDatabase(builder.Configuration.GetConnectionString("Default"))
    .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly, s => s.StartsWith("Migrations.") && s.EndsWith(".sql"))
    .JournalToPostgresqlTable("public", "schemaversions_shop")
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

var shop = app.MapGroup("/api/v1/shop").AddEndpointFilter<ShopExceptionFilter>();
shop.MapProductEndpoints();
shop.MapCartEndpoints();
shop.MapCheckoutEndpoints();
shop.MapOrderEndpoints();
shop.MapAdminProductEndpoints();

app.Run();

// Exposes the top-level-statements Program class to WebApplicationFactory<Program> in
// OMyFish.ShopService.Tests — top-level statements otherwise generate it `internal`, which
// isn't visible across assemblies.
public partial class Program;

// Logs and turns any exception the endpoints/middleware don't already handle into a clean
// JSON 5xx response, instead of ASP.NET Core's default unhandled-exception behavior.
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
