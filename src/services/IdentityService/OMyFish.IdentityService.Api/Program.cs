using System.Text;
using Adyen.Checkout.Extensions;
using Adyen.Checkout.Services;
using Adyen.Core.Options;
using Adyen.Webhooks.Extensions;
using Adyen.Webhooks.Handlers;
using DbUp;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OMyFish.IdentityService.Api.Endpoints;
using OMyFish.IdentityService.Application;
using OMyFish.IdentityService.Application.Commands;
using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.IdentityService.Domain.Interfaces;
using OMyFish.IdentityService.Infrastructure.Auth;
using OMyFish.IdentityService.Infrastructure.Payments;
using OMyFish.IdentityService.Infrastructure.Persistence;
using OMyFish.IdentityService.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] identity | {Message:lj}{NewLine}{Exception}"));

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<IdentityDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
builder.Services.AddScoped<IIdempotencyKeyRepository, IdempotencyKeyRepository>();
builder.Services.AddScoped<IProcessedWebhookEventRepository, ProcessedWebhookEventRepository>();

// ── CQRS ──────────────────────────────────────────────────────────────────────
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(RegisterCommand).Assembly));

// ── Payment processors (test keys) — billing endpoints return 503 until configured ────────
builder.Services.AddSingleton(sp => new StripePaymentGateway(
    secretKey: builder.Configuration["Stripe__SecretKey"] ?? "",
    webhookSecret: builder.Configuration["Stripe__WebhookSecret"] ?? "",
    prices: new Dictionary<string, string>
    {
        ["monthly"] = builder.Configuration["Stripe__PriceMonthly"] ?? "",  // 5 CAD/month
        ["yearly"] = builder.Configuration["Stripe__PriceYearly"] ?? "",    // 29 CAD/year
    },
    logger: sp.GetRequiredService<ILogger<StripePaymentGateway>>()));
builder.Services.AddHttpClient("paypal", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["PayPal__BaseUrl"] ?? "https://api-m.sandbox.paypal.com");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton(sp => new PayPalPaymentGateway(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("paypal"),
    clientId: builder.Configuration["PayPal__ClientId"] ?? "",
    clientSecret: builder.Configuration["PayPal__ClientSecret"] ?? "",
    webhookId: builder.Configuration["PayPal__WebhookId"] ?? "",
    returnUrl: builder.Configuration["PayPal__ReturnUrl"] ?? "",
    cancelUrl: builder.Configuration["PayPal__CancelUrl"] ?? "",
    planIds: new Dictionary<string, string>
    {
        ["monthly"] = builder.Configuration["PayPal__PlanMonthly"] ?? "",
        ["yearly"] = builder.Configuration["PayPal__PlanYearly"] ?? "",
    }));
// Adyen's SDK (36.1.0) registers its typed services via these IHostBuilder extensions rather
// than plain IServiceCollection.AddSingleton — the shape its own README documents, and the one
// that actually compiles against this version (see BACKLOG.md item I.4 for the version that
// doesn't).
builder.Host.ConfigureCheckout((context, services, config) =>
{
    config.ConfigureAdyenOptions(options =>
    {
        options.AdyenApiKey = context.Configuration["Adyen__ApiKey"] ?? "";
        options.Environment = (context.Configuration["Adyen__Environment"] ?? "Test") == "Live"
            ? AdyenEnvironment.Live : AdyenEnvironment.Test;
        var liveUrlPrefix = context.Configuration["Adyen__LiveEndpointUrlPrefix"];
        if (!string.IsNullOrEmpty(liveUrlPrefix)) options.LiveEndpointUrlPrefix = liveUrlPrefix;
    });
    services.AddPaymentsService();
    services.AddModificationsService();
});
builder.Host.ConfigureWebhooks((context, services, config) =>
{
    config.ConfigureAdyenOptions(options =>
    {
        options.AdyenHmacKey = context.Configuration["Adyen__HmacKey"] ?? "";
    });
    services.AddWebhooksHandler();
});
builder.Services.AddSingleton(sp => new AdyenPaymentGateway(
    sp.GetRequiredService<IPaymentsService>(),
    sp.GetRequiredService<IModificationsService>(),
    sp.GetRequiredService<IWebhooksHandler>(),
    apiKey: builder.Configuration["Adyen__ApiKey"] ?? "",
    merchantAccount: builder.Configuration["Adyen__MerchantAccount"] ?? "",
    hmacKey: builder.Configuration["Adyen__HmacKey"] ?? "",
    currency: builder.Configuration["Adyen__Currency"] ?? "CAD",
    priceMonthlyCents: long.Parse(builder.Configuration["Adyen__PriceMonthlyCents"] ?? "500"),
    priceYearlyCents: long.Parse(builder.Configuration["Adyen__PriceYearlyCents"] ?? "2900"),
    returnUrl: builder.Configuration["Adyen__ReturnUrl"] ?? ""));
builder.Services.AddSingleton(sp => new PaymentProcessorRegistry(
    [
        sp.GetRequiredService<StripePaymentGateway>(),
        sp.GetRequiredService<PayPalPaymentGateway>(),
        sp.GetRequiredService<AdyenPaymentGateway>(),
    ],
    defaultName: builder.Configuration["Payment__DefaultProcessor"] ?? "stripe"));
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<ReconciliationService>();

// ── JWT ───────────────────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt__Secret"] ?? builder.Configuration["Jwt:Secret"];
if (builder.Environment.IsProduction() && (jwtSecret is null || jwtSecret.StartsWith("dev-secret")))
    throw new InvalidOperationException("Jwt__Secret must be set to a non-default value in production.");
jwtSecret ??= "dev-secret-change-in-production-min-32-chars";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
builder.Services.AddSingleton<ITokenService>(_ => new JwtTokenService(signingKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero,
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
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("identity-service"))
        .AddAspNetCoreInstrumentation(opts => opts.RecordException = true)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opts => opts.Endpoint = new Uri(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://jaeger:4317")));

var app = builder.Build();

// ── Auto-migrate on startup ───────────────────────────────────────────────────
// Applies migrations/IdentityService/*.sql (embedded at build time) via DbUp instead of
// EF's EnsureCreated, which only creates schema on an empty DB and silently no-ops
// otherwise — that gap caused a real production-shaped incident (a new required column
// went live with no matching schema change). Fails fast on migration failure rather than
// starting against a schema the app doesn't match (BACKLOG.md item F, WEAKNESS_AUDIT.md §3.1).
var migrator = DeployChanges.To
    .PostgresqlDatabase(builder.Configuration.GetConnectionString("Default"))
    .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly, s => s.StartsWith("Migrations.") && s.EndsWith(".sql"))
    .JournalToPostgresqlTable("public", "schemaversions_identity")
    .LogToConsole()
    .Build();
var migrationResult = migrator.PerformUpgrade();
if (!migrationResult.Successful)
    throw new InvalidOperationException("Database migration failed — see inner exception.", migrationResult.Error);

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseHttpMetrics();

// ── Endpoints ─────────────────────────────────────────────────────────────────

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "identity" }));
app.MapMetrics("/metrics");
app.MapOpenApi();
app.MapScalarApiReference();

app.MapAuthEndpoints();
app.MapBillingEndpoints();
app.MapAdminEndpoints();

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
