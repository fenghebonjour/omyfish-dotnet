using System.Text;
using Adyen.Checkout.Extensions;
using Adyen.Checkout.Services;
using Adyen.Core.Options;
using Adyen.Webhooks.Extensions;
using Adyen.Webhooks.Handlers;
using Confluent.Kafka;
using DbUp;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Caching.Memory;
using Minio;
using OMyFish.ShopService.Api;
using OMyFish.ShopService.Api.Endpoints;
using OMyFish.ShopService.Application;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Infrastructure.Caching;
using OMyFish.ShopService.Infrastructure.Messaging;
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

// Read replica for catalog reads (ProductCatalogReader); falls back to the primary when
// ConnectionStrings:Replica isn't set.
builder.Services.AddDbContext<ShopReadDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Replica")
                   ?? builder.Configuration.GetConnectionString("Default")));

// Two-level product catalog cache: in-process L1 + shared Redis L2 (Redis:ConnectionString;
// L1-only when unset). AbortOnConnectFail=false so a Redis that's down at startup doesn't stop
// the service — the cache just falls through to the database until it reconnects.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IProductCatalogCache>(sp =>
{
    var redisConnection = builder.Configuration["Redis:ConnectionString"];
    IConnectionMultiplexer? redis = null;
    if (!string.IsNullOrEmpty(redisConnection))
    {
        var options = ConfigurationOptions.Parse(redisConnection);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 2000;
        options.SyncTimeout = 1000;
        options.AsyncTimeout = 1000;
        redis = ConnectionMultiplexer.Connect(options);
    }
    return new ProductCatalogCache(
        sp.GetRequiredService<IMemoryCache>(), redis, sp.GetRequiredService<ILogger<ProductCatalogCache>>());
});

// Repositories
builder.Services.AddScoped<IProductCatalogReader, ProductCatalogReader>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ICartRepository, CartRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IIdempotencyKeyRepository, IdempotencyKeyRepository>();

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

// PayPal (Orders v2) and Adyen (Checkout Sessions) — sibling processors to Stripe, same as
// IdentityService's billing. Client id/secret and API key/merchant account are account-level
// credentials shareable with IdentityService; the webhook id / HMAC key must be this service's
// own (a separate webhook subscription for /api/v1/shop/webhook/{paypal,adyen}).
builder.Services.AddHttpClient("paypal", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["PayPal:BaseUrl"] ?? "https://api-m.sandbox.paypal.com");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton(sp => new PayPalCheckoutGateway(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("paypal"),
    clientId: builder.Configuration["PayPal:ClientId"] ?? "",
    clientSecret: builder.Configuration["PayPal:ClientSecret"] ?? "",
    webhookId: builder.Configuration["PayPal:ShopWebhookId"] ?? "",
    currency: builder.Configuration["PayPal:ShopCurrency"] ?? "cad"));
builder.Host.ConfigureCheckout((context, services, config) =>
{
    config.ConfigureAdyenOptions(options =>
    {
        options.AdyenApiKey = context.Configuration["Adyen:ApiKey"] ?? "";
        options.Environment = (context.Configuration["Adyen:Environment"] ?? "Test") == "Live"
            ? AdyenEnvironment.Live : AdyenEnvironment.Test;
        var liveUrlPrefix = context.Configuration["Adyen:LiveEndpointUrlPrefix"];
        if (!string.IsNullOrEmpty(liveUrlPrefix)) options.LiveEndpointUrlPrefix = liveUrlPrefix;
    });
    services.AddPaymentsService();
    services.AddModificationsService();
});
builder.Host.ConfigureWebhooks((context, services, config) =>
{
    config.ConfigureAdyenOptions(options => options.AdyenHmacKey = context.Configuration["Adyen:ShopHmacKey"] ?? "");
    services.AddWebhooksHandler();
});
builder.Services.AddSingleton(sp => new AdyenCheckoutGateway(
    sp.GetRequiredService<IPaymentsService>(),
    sp.GetRequiredService<IModificationsService>(),
    sp.GetRequiredService<IWebhooksHandler>(),
    apiKey: builder.Configuration["Adyen:ApiKey"] ?? "",
    merchantAccount: builder.Configuration["Adyen:MerchantAccount"] ?? "",
    hmacKey: builder.Configuration["Adyen:ShopHmacKey"] ?? "",
    currency: builder.Configuration["Adyen:ShopCurrency"] ?? "cad"));
// Load-test-only WireMock stand-in (docker-compose.loadtest.yml); absent from a normal run.
var mockPaymentsEnabled = builder.Configuration.GetValue("Payment:Mock:Enabled", false);
if (mockPaymentsEnabled)
    builder.Services.AddHttpClient<MockPaymentCheckoutGateway>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Payment:Mock:BaseUrl"] ?? "http://wiremock:8080");
        client.Timeout = TimeSpan.FromSeconds(15);
    });

// Each processor behind its own retry/circuit-breaker/bulkhead pipeline (ResilientPaymentGateway).
builder.Services.AddSingleton(sp => new ShopPaymentProcessorRegistry(
    [
        new ResilientPaymentGateway(sp.GetRequiredService<StripeCheckoutGateway>()),
        new ResilientPaymentGateway(sp.GetRequiredService<PayPalCheckoutGateway>()),
        new ResilientPaymentGateway(sp.GetRequiredService<AdyenCheckoutGateway>()),
        .. mockPaymentsEnabled
            ? [new ResilientPaymentGateway(sp.GetRequiredService<MockPaymentCheckoutGateway>())]
            : Array.Empty<IPaymentGateway>(),
    ],
    defaultName: builder.Configuration["Payment:ShopDefaultProcessor"] ?? "stripe"));

// PayPal/Adyen have no abandoned-checkout webhook — this sweep restocks those orders.
builder.Services.AddHostedService<StaleOrderSweepService>();

// Kafka order-event stream (omyfish.shop.orders): publisher + this service's own audit-ledger
// consumer. Off (no-op publisher, no consumer) when Kafka:BootstrapServers isn't set.
var kafkaBootstrap = builder.Configuration["Kafka:BootstrapServers"];
if (string.IsNullOrEmpty(kafkaBootstrap))
{
    builder.Services.AddSingleton<IOrderEventPublisher, NoOpOrderEventPublisher>();
}
else
{
    builder.Services.AddSingleton<IProducer<string, string>>(_ => new ProducerBuilder<string, string>(
        new ProducerConfig
        {
            BootstrapServers = kafkaBootstrap,
            Acks = Acks.All,
            EnableIdempotence = true,
            // Bounds how long an undeliverable event sits in librdkafka's queue (broker down).
            MessageTimeoutMs = 30_000,
        }).Build());
    builder.Services.AddSingleton<IOrderEventPublisher, KafkaOrderEventPublisher>();
    builder.Services.AddHostedService(sp => new OrderEventLedgerConsumer(
        kafkaBootstrap, sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<ILogger<OrderEventLedgerConsumer>>()));
}

// Checkout kill switch (toggled via POST /api/v1/shop/admin/feature-flags/checkout).
builder.Services.AddSingleton<FeatureFlags>();

// Admission control on /checkout, independent of the per-processor breakers: caps new checkout
// attempts this instance admits per second, rejecting the overflow immediately (no queue) with
// a cheap 429 instead of spending a DB connection and a stock reservation on it. Global, not
// per-IP — same as omyfish-java's resilience4j "checkout" rate limiter (50/s, provisional
// until load-tested).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("checkout", o =>
    {
        o.PermitLimit = builder.Configuration.GetValue("Shop:Checkout:PermitsPerSecond", 50);
        o.Window = TimeSpan.FromSeconds(1);
        o.QueueLimit = 0;
    });
});

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
// After UseHttpMetrics, so rate-limited 429s still show up in http_requests_received_total.
app.UseRateLimiter();

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
shop.MapAdminFeatureFlagEndpoints();

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
