using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OMyFish.ObservationService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace OMyFish.ObservationService.Tests;

// Endpoint-level slice test host: a real WebApplicationFactory<Program> wired to real
// Postgres + RabbitMQ via Testcontainers (neither mocked) — the observation-create endpoint
// publishes through MassTransit's EF Core transactional outbox (BACKLOG.md item F §2.3), and
// only a real broker can prove the outbox actually drains, not just that a DB row got written.
public class ObservationApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
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

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public ObservationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ObservationDbContext>().UseNpgsql(PostgresConnectionString).Options);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

        // Forces the host to build now (runs DbUp migrations, connects to the real broker) so
        // a wiring failure surfaces at fixture setup instead of inside the first test.
        _ = Services;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
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
        }));
    }
}

[CollectionDefinition("ObservationApi")]
public class ObservationApiCollection : ICollectionFixture<ObservationApiFixture>;
