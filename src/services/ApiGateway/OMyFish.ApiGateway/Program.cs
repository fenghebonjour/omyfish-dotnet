using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

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

// Catches unhandled exceptions so callers get a clean 5xx instead of a raw exception page
// (BACKLOG.md item F, WEAKNESS_AUDIT.md §2.2).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("api-gateway"))
        .AddAspNetCoreInstrumentation(opts => opts.RecordException = true)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opts => opts.Endpoint = new Uri(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://jaeger:4317")));

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Restricted to the known frontend origins (comma-separated, so the React frontend on :3000
// and the Angular twin on :4200 can run side by side). AllowCredentials is required now that
// the refresh token travels as an httpOnly cookie instead of the response body
// (BACKLOG.md item F, WEAKNESS_AUDIT.md §1.3) — safe only because every origin is a specific
// known value, never a wildcard, per CORS rules.
var allowedOrigins = (builder.Configuration["Cors__AllowedOrigin"]
                   ?? builder.Configuration["Cors:AllowedOrigin"]
                   ?? "http://localhost:3000,http://localhost:4200")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(opts => opts.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyMethod()
    .AllowAnyHeader()
    .AllowCredentials()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseHttpMetrics();

// Deny by default: routes must opt out via "AuthorizationPolicy": "Anonymous" in
// appsettings.json (identity-route for register/login, species-route/species-identify-route
// which are intentionally public product surfaces). Previously the gateway configured JWT
// auth but never enforced it, leaving enforcement entirely to downstream services with no
// second layer (BACKLOG.md item F, WEAKNESS_AUDIT.md §1.1).
app.MapReverseProxy().RequireAuthorization();

app.MapGet("/health", () => "ok");
app.MapMetrics("/metrics");
app.Run();

// Logs and turns any exception the middleware/proxy doesn't already handle into a clean
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

// Exposes the top-level-statements Program class to WebApplicationFactory<Program> in
// OMyFish.ApiGateway.Tests — top-level statements otherwise generate it `internal`, which
// isn't visible across assemblies.
public partial class Program;
