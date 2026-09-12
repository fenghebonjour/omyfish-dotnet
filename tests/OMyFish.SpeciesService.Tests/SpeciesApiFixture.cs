using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using OMyFish.SpeciesService.Application.Interfaces;
using OMyFish.SpeciesService.Infrastructure.Persistence;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace OMyFish.SpeciesService.Tests;

// Endpoint-level slice test host: a real WebApplicationFactory<Program> wired to real
// Postgres + RabbitMQ via Testcontainers — only the two genuinely-external dependencies
// (the ai-service HTTP call, MinIO object storage) are faked. The /identify endpoint
// publishes through MassTransit's EF Core transactional outbox (BACKLOG.md item F §2.3), and
// only a real broker can prove the outbox actually drains, not just that a DB row got written.
public class SpeciesApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4-alpine")
        .WithDatabase("omyfish")
        .WithUsername("omyfish")
        .WithPassword("omyfish_dev")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:3.13-management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7").Build();

    public FakeAIServiceClient FakeAi { get; } = new();
    public FakeStorageService FakeStorage { get; } = new();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public SpeciesDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<SpeciesDbContext>().UseNpgsql(PostgresConnectionString).Options);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync(), _mongo.StartAsync());

        // Forces the host to build now (runs DbUp migrations, connects to the real broker) so
        // a wiring failure surfaces at fixture setup instead of inside the first test.
        _ = Services;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
        await _mongo.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
            ["RabbitMQ:Host"] = _rabbitMq.Hostname,
            ["RabbitMQ:Port"] = _rabbitMq.GetMappedPublicPort(5672).ToString(),
            ["RabbitMQ:Username"] = "guest",
            ["RabbitMQ:Password"] = "guest",
            ["MongoDB:ConnectionString"] = _mongo.GetConnectionString(),
            ["MongoDB:Database"] = "omyfish",
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAIServiceClient>();
            services.AddSingleton<IAIServiceClient>(FakeAi);
            services.RemoveAll<IStorageService>();
            services.AddSingleton<IStorageService>(FakeStorage);
        });
    }
}

[CollectionDefinition("SpeciesApi")]
public class SpeciesApiCollection : ICollectionFixture<SpeciesApiFixture>;
