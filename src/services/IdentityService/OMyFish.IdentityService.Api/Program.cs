using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Domain.Interfaces;
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

// ── JWT ───────────────────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt__Secret"]
    ?? builder.Configuration["Jwt:Secret"]
    ?? "dev-secret-change-in-production-min-32-chars";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

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

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("identity-service"))
        .AddAspNetCoreInstrumentation(opts => opts.RecordException = true)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opts => opts.Endpoint = new Uri(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://jaeger:4317")));

var app = builder.Build();

// ── Auto-migrate on startup ───────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "DB not ready on startup — migrations may be needed");
    }
}

app.UseAuthentication();
app.UseAuthorization();
app.UseHttpMetrics();

// ── Endpoints ─────────────────────────────────────────────────────────────────

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "identity" }));
app.MapMetrics("/metrics");

app.MapPost("/api/v1/auth/register", async (RegisterRequest req, IUserRepository repo) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Email and password are required." });

    if (await repo.FindByEmailAsync(req.Email) is not null)
        return Results.Conflict(new { error = "Email already registered." });

    var hashed = BCrypt.Net.BCrypt.HashPassword(req.Password);
    var user = User.Create(req.Email, hashed, req.DisplayName);
    await repo.CreateAsync(user);

    return Results.Created($"/api/v1/auth/me",
        new UserDto(user.Id, user.Email, user.DisplayName, user.Role));
});

app.MapPost("/api/v1/auth/login", async (LoginRequest req, IUserRepository repo) =>
{
    var user = await repo.FindByEmailAsync(req.Email);
    if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.HashedPassword))
        return Results.Unauthorized();
    if (!user.IsActive)
        return Results.Forbid();

    var accessToken = CreateJwt(user, signingKey, TimeSpan.FromDays(1));
    var refreshToken = CreateRefreshJwt(user, signingKey);
    return Results.Ok(new TokenResponse(accessToken, refreshToken, user.Id, user.Email, user.Role));
});

app.MapPost("/api/v1/auth/refresh", async (RefreshRequest req, IUserRepository repo) =>
{
    var handler = new JwtSecurityTokenHandler();
    ClaimsPrincipal principal;
    try
    {
        principal = handler.ValidateToken(req.RefreshToken, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
        }, out _);
    }
    catch { return Results.Unauthorized(); }

    var typeClaim = principal.FindFirstValue("token_type");
    if (typeClaim != "refresh") return Results.Unauthorized();

    var idStr = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(idStr, out var userId)) return Results.Unauthorized();

    var user = await repo.FindByIdAsync(userId);
    if (user is null || !user.IsActive) return Results.Unauthorized();

    var accessToken = CreateJwt(user, signingKey, TimeSpan.FromDays(1));
    var refreshToken = CreateRefreshJwt(user, signingKey);
    return Results.Ok(new TokenResponse(accessToken, refreshToken, user.Id, user.Email, user.Role));
});

app.MapGet("/api/v1/auth/me", async (ClaimsPrincipal principal, IUserRepository repo) =>
{
    var idStr = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(idStr, out var id)) return Results.Unauthorized();
    var user = await repo.FindByIdAsync(id);
    return user is null ? Results.NotFound() :
        Results.Ok(new UserDto(user.Id, user.Email, user.DisplayName, user.Role));
}).RequireAuthorization();

app.Run();

// ── Helpers ───────────────────────────────────────────────────────────────────
static string CreateJwt(User user, SymmetricSecurityKey key, TimeSpan lifetime)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Role, user.Role),
        new Claim("token_type", "access"),
    };
    var token = new JwtSecurityToken(
        claims: claims,
        expires: DateTime.UtcNow.Add(lifetime),
        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(token);
}

static string CreateRefreshJwt(User user, SymmetricSecurityKey key)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim("token_type", "refresh"),
    };
    var token = new JwtSecurityToken(
        claims: claims,
        expires: DateTime.UtcNow.AddDays(30),
        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(token);
}

// ── Request/Response records ──────────────────────────────────────────────────
record RegisterRequest(string Email, string Password, string? DisplayName);
record LoginRequest(string Email, string Password);
record RefreshRequest(string RefreshToken);
record TokenResponse(string AccessToken, string RefreshToken, Guid UserId, string Email, string Role);
record UserDto(Guid Id, string Email, string? DisplayName, string Role);
