using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Domain.Interfaces;
using OMyFish.IdentityService.Infrastructure.Persistence;
using OMyFish.IdentityService.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

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
    .AddJwtBearer(opts => opts.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey,
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero,
    });
builder.Services.AddAuthorization();

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

// ── Endpoints ─────────────────────────────────────────────────────────────────

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "identity" }));

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

    var token = CreateJwt(user, signingKey, TimeSpan.FromDays(1));
    return Results.Ok(new TokenResponse(token, user.Id, user.Email, user.Role));
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
    };
    var token = new JwtSecurityToken(
        claims: claims,
        expires: DateTime.UtcNow.Add(lifetime),
        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(token);
}

// ── Request/Response records ──────────────────────────────────────────────────
record RegisterRequest(string Email, string Password, string? DisplayName);
record LoginRequest(string Email, string Password);
record TokenResponse(string AccessToken, Guid UserId, string Email, string Role);
record UserDto(Guid Id, string Email, string? DisplayName, string Role);
