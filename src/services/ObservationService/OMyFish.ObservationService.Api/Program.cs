using System.Text;
using MassTransit;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
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
    x.UsingRabbitMq((ctx, cfg) =>
    {
        var host = builder.Configuration["RabbitMQ__Host"]
                ?? builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq";
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

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("observation-service"))
        .AddAspNetCoreInstrumentation(opts => opts.RecordException = true)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opts => opts.Endpoint = new Uri(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://jaeger:4317")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ObservationDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
    catch { }
}

app.UseAuthentication();
app.UseAuthorization();
app.UseHttpMetrics();

app.MapGet("/health", () => "ok");
app.MapMetrics("/metrics");
app.MapObservationEndpoints();

app.Run();
