using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OMyFish.SpeciesService.Application.Commands;
using OMyFish.SpeciesService.Application.Interfaces;
using OMyFish.SpeciesService.Api.Endpoints;
using OMyFish.SpeciesService.Infrastructure.ExternalServices;
using OMyFish.SpeciesService.Infrastructure.Messaging;
using OMyFish.SpeciesService.Infrastructure.Persistence;
using OMyFish.SpeciesService.Infrastructure.Repositories;
using OMyFish.Shared.BuildingBlocks.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<SpeciesDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Repositories
builder.Services.AddScoped<ISpeciesRepository, SpeciesRepository>();

// CQRS
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(IdentifyFishCommand).Assembly));

// AI service HTTP client
builder.Services.AddHttpClient<IAIServiceClient, AIServiceClient>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["AIService__BaseUrl"]
        ?? builder.Configuration["AIService:BaseUrl"]
        ?? "http://ai-service:8000"));

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
var jwtSecret = builder.Configuration["Jwt__Secret"]
             ?? builder.Configuration["Jwt:Secret"]
             ?? "dev-secret-please-replace-in-production";
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

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => "ok");
app.MapSpeciesEndpoints();
app.MapIdentificationEndpoints();

app.Run();
