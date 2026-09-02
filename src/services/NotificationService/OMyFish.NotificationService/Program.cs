using System.Security.Claims;
using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OMyFish.NotificationService.Consumers;
using OMyFish.NotificationService.Persistence;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] notifications | {Message:lj}{NewLine}{Exception}"));

builder.Services.AddDbContext<NotificationDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<FishIdentifiedConsumer>();
    x.AddConsumer<ObservationCreatedConsumer>();

    x.UsingRabbitMq((busCtx, cfg) =>
    {
        var rabbitHost = builder.Configuration["RabbitMQ__Host"]
                      ?? builder.Configuration["RabbitMQ:Host"]
                      ?? "rabbitmq";
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ__Username"]
                    ?? builder.Configuration["RabbitMQ:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ__Password"]
                    ?? builder.Configuration["RabbitMQ:Password"] ?? "guest");
        });

        cfg.ReceiveEndpoint("notifications.fish-identified", e =>
        {
            e.ConfigureConsumer<FishIdentifiedConsumer>(busCtx);
            e.UseMessageRetry(r => r.Exponential(3,
                TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30)));
        });

        cfg.ReceiveEndpoint("notifications.observation-created", e =>
        {
            e.ConfigureConsumer<ObservationCreatedConsumer>(busCtx);
            e.UseMessageRetry(r => r.Exponential(3,
                TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30)));
        });
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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
    catch { }
}

app.UseAuthentication();
app.UseAuthorization();
app.UseHttpMetrics();

app.MapGet("/health", () => "ok");
app.MapMetrics("/metrics");
app.MapOpenApi();
app.MapScalarApiReference();

var group = app.MapGroup("/api/v1/notifications").RequireAuthorization();

group.MapGet("/", async (HttpContext ctx, NotificationDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(ctx);
    if (userId == Guid.Empty) return Results.Unauthorized();

    var notifications = await db.Notifications
        .Where(n => n.UserId == userId)
        .OrderByDescending(n => n.CreatedAt)
        .Select(n => new NotificationDto(n.Id, n.UserId, n.Type, n.Title, n.Body, n.IsRead, n.CreatedAt))
        .ToListAsync(ct);
    return Results.Ok(notifications);
});

group.MapPut("/{id:guid}/read", async (Guid id, HttpContext ctx, NotificationDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(ctx);
    if (userId == Guid.Empty) return Results.Unauthorized();

    var notification = await db.Notifications
        .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
    if (notification is null) return Results.NotFound();

    notification.MarkRead();
    await db.SaveChangesAsync(ct);
    return Results.Ok();
});

app.Run();

static Guid GetUserId(HttpContext ctx)
{
    var sub = ctx.User.FindFirst("sub")?.Value
           ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
}

public sealed record NotificationDto(
    Guid Id, Guid UserId, string Type, string Title, string? Body, bool IsRead, DateTime CreatedAt);
