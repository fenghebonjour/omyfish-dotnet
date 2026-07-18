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
builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();

// ── Stripe (test keys) — billing endpoints return 503 until configured ────────
var stripeSecretKey = builder.Configuration["Stripe__SecretKey"] ?? "";
var stripeWebhookSecret = builder.Configuration["Stripe__WebhookSecret"] ?? "";
var stripePrices = new Dictionary<string, string>
{
    ["monthly"] = builder.Configuration["Stripe__PriceMonthly"] ?? "",  // 5 CAD/month
    ["yearly"] = builder.Configuration["Stripe__PriceYearly"] ?? "",    // 29 CAD/year
};
var appBaseUrl = builder.Configuration["App__BaseUrl"] ?? "http://localhost:3000";

// ── JWT ───────────────────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt__Secret"] ?? builder.Configuration["Jwt:Secret"];
if (builder.Environment.IsProduction() && (jwtSecret is null || jwtSecret.StartsWith("dev-secret")))
    throw new InvalidOperationException("Jwt__Secret must be set to a non-default value in production.");
jwtSecret ??= "dev-secret-change-in-production-min-32-chars";
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

app.MapPost("/api/v1/auth/register", async (RegisterRequest req, IUserRepository repo, ISubscriptionRepository subs) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Email and password are required." });

    if (await repo.FindByEmailAsync(req.Email) is not null)
        return Results.Conflict(new { error = "Email already registered." });

    var hashed = BCrypt.Net.BCrypt.HashPassword(req.Password);
    var user = User.Create(req.Email, hashed, req.DisplayName);
    await repo.CreateAsync(user);
    await subs.CreateAsync(Subscription.StartTrial(user.Id));

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

// ── Billing ───────────────────────────────────────────────────────────────────

app.MapGet("/api/v1/billing/me", async (ClaimsPrincipal principal, ISubscriptionRepository subs) =>
{
    if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();
    var sub = await subs.FindByUserIdAsync(userId);
    if (sub is null)
    {
        sub = Subscription.StartTrial(userId);
        await subs.CreateAsync(sub);
    }
    return Results.Ok(new SubscriptionDto(
        sub.EffectiveStatus, sub.Plan, sub.TrialEnd, sub.CurrentPeriodEnd));
}).RequireAuthorization();

app.MapPost("/api/v1/billing/checkout", async (
    CheckoutRequest req, ClaimsPrincipal principal, IUserRepository users) =>
{
    if (!stripePrices.TryGetValue(req.Plan, out var priceId))
        return Results.BadRequest(new { error = "plan must be monthly or yearly" });
    if (string.IsNullOrEmpty(stripeSecretKey) || string.IsNullOrEmpty(priceId))
        return Results.Problem("Stripe is not configured.", statusCode: 503);

    if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();
    var user = await users.FindByIdAsync(userId);
    if (user is null) return Results.NotFound();

    var options = new Stripe.Checkout.SessionCreateOptions
    {
        Mode = "subscription",
        CustomerEmail = user.Email,
        ClientReferenceId = userId.ToString(),
        LineItems = [new() { Price = priceId, Quantity = 1 }],
        SuccessUrl = $"{appBaseUrl}/account?billing=success",
        CancelUrl = $"{appBaseUrl}/account?billing=canceled",
        Metadata = new() { ["user_id"] = userId.ToString(), ["plan"] = req.Plan },
    };
    var session = await new Stripe.Checkout.SessionService(
        new Stripe.StripeClient(stripeSecretKey)).CreateAsync(options);
    return Results.Ok(new { checkoutUrl = session.Url });
}).RequireAuthorization();

app.MapPost("/api/v1/billing/webhook", async (HttpRequest request, ISubscriptionRepository subs) =>
{
    if (string.IsNullOrEmpty(stripeWebhookSecret))
        return Results.Problem("Stripe webhook is not configured.", statusCode: 503);

    var payload = await new StreamReader(request.Body).ReadToEndAsync();
    Stripe.Event stripeEvent;
    try
    {
        stripeEvent = Stripe.EventUtility.ConstructEvent(
            payload, request.Headers["Stripe-Signature"], stripeWebhookSecret);
    }
    catch { return Results.BadRequest(new { error = "Invalid webhook signature" }); }

    switch (stripeEvent.Type)
    {
        case "checkout.session.completed":
        {
            var session = (Stripe.Checkout.Session)stripeEvent.Data.Object;
            if (Guid.TryParse(session.ClientReferenceId, out var userId))
            {
                var sub = await subs.FindByUserIdAsync(userId);
                if (sub is null) { sub = Subscription.StartTrial(userId); await subs.CreateAsync(sub); }
                sub.Activate(
                    session.Metadata.GetValueOrDefault("plan", "monthly"),
                    periodEnd: null,  // authoritative period end arrives on subscription.updated
                    stripeCustomerId: session.CustomerId,
                    stripeSubscriptionId: session.SubscriptionId);
                await subs.SaveChangesAsync();
            }
            break;
        }
        case "customer.subscription.updated":
        case "customer.subscription.deleted":
        {
            var stripeSub = (Stripe.Subscription)stripeEvent.Data.Object;
            var sub = await subs.FindByStripeCustomerIdAsync(stripeSub.CustomerId);
            if (sub is not null)
            {
                if (stripeEvent.Type == "customer.subscription.deleted"
                    || stripeSub.Status is "canceled" or "unpaid")
                    sub.Cancel();
                else
                    sub.Activate(sub.Plan ?? "monthly",
                        stripeSub.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd,
                        stripeSubscriptionId: stripeSub.Id);
                await subs.SaveChangesAsync();
            }
            break;
        }
    }
    return Results.Ok(new { handled = true });
});

// ── Admin ─────────────────────────────────────────────────────────────────────

app.MapGet("/api/v1/admin/stats", async (
    ISubscriptionRepository subs, IdentityDbContext db) =>
{
    var userCount = await db.Users.CountAsync();
    var all = await subs.GetAllAsync();
    var byStatus = all.GroupBy(s => s.EffectiveStatus)
        .ToDictionary(g => g.Key, g => g.Count());
    var activeMonthly = all.Count(s => s.EffectiveStatus == "active" && s.Plan == "monthly");
    var activeYearly = all.Count(s => s.EffectiveStatus == "active" && s.Plan == "yearly");
    return Results.Ok(new
    {
        users = userCount,
        subscriptions = byStatus,
        activePlans = new { monthly = activeMonthly, yearly = activeYearly },
        mrrCad = Math.Round(activeMonthly * 5 + activeYearly * 29 / 12.0, 2),
    });
}).RequireAuthorization(policy => policy.RequireRole("admin"));

app.MapGet("/api/v1/admin/subscriptions", async (
    ISubscriptionRepository subs, IdentityDbContext db) =>
{
    var all = await subs.GetAllAsync();
    var emails = await db.Users.AsNoTracking()
        .ToDictionaryAsync(u => u.Id, u => u.Email);
    return Results.Ok(all.Select(s => new
    {
        s.UserId,
        Email = emails.GetValueOrDefault(s.UserId, "?"),
        Status = s.EffectiveStatus,
        s.Plan,
        s.TrialEnd,
        s.CurrentPeriodEnd,
    }));
}).RequireAuthorization(policy => policy.RequireRole("admin"));

app.MapPost("/api/v1/admin/subscriptions/{userId:guid}/grant", async (
    Guid userId, GrantRequest? req, ISubscriptionRepository subs) =>
{
    var sub = await subs.FindByUserIdAsync(userId);
    if (sub is null) { sub = Subscription.StartTrial(userId); await subs.CreateAsync(sub); }
    sub.Activate(req?.Plan ?? "yearly", DateTime.UtcNow.AddDays(req?.Days ?? 365));
    await subs.SaveChangesAsync();
    return Results.Ok(new SubscriptionDto(
        sub.EffectiveStatus, sub.Plan, sub.TrialEnd, sub.CurrentPeriodEnd));
}).RequireAuthorization(policy => policy.RequireRole("admin"));

app.MapPost("/api/v1/admin/subscriptions/{userId:guid}/revoke", async (
    Guid userId, ISubscriptionRepository subs) =>
{
    var sub = await subs.FindByUserIdAsync(userId);
    if (sub is null) return Results.NotFound();
    sub.Cancel();
    await subs.SaveChangesAsync();
    return Results.Ok(new SubscriptionDto(
        sub.EffectiveStatus, sub.Plan, sub.TrialEnd, sub.CurrentPeriodEnd));
}).RequireAuthorization(policy => policy.RequireRole("admin"));

app.MapPost("/api/v1/admin/subscriptions/{userId:guid}/extend-trial", async (
    Guid userId, GrantRequest? req, ISubscriptionRepository subs) =>
{
    var sub = await subs.FindByUserIdAsync(userId);
    if (sub is null) { sub = Subscription.StartTrial(userId); await subs.CreateAsync(sub); }
    sub.ExtendTrial(req?.Days ?? 7);
    await subs.SaveChangesAsync();
    return Results.Ok(new SubscriptionDto(
        sub.EffectiveStatus, sub.Plan, sub.TrialEnd, sub.CurrentPeriodEnd));
}).RequireAuthorization(policy => policy.RequireRole("admin"));

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
record CheckoutRequest(string Plan);
record GrantRequest(int? Days, string? Plan);
record SubscriptionDto(string Status, string? Plan, DateTime? TrialEnd, DateTime? CurrentPeriodEnd);
record LoginRequest(string Email, string Password);
record RefreshRequest(string RefreshToken);
record TokenResponse(string AccessToken, string RefreshToken, Guid UserId, string Email, string Role);
record UserDto(Guid Id, string Email, string? DisplayName, string Role);
