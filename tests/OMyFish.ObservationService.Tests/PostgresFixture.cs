using DbUp;
using Microsoft.EntityFrameworkCore;
using OMyFish.ObservationService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace OMyFish.ObservationService.Tests;

// Real Postgres+PostGIS via Testcontainers, migrated with this repo's actual raw-SQL
// migrations — the same image and migration files `make build-up` applies, so schema/entity
// drift (see BACKLOG.md item F §2.3/§3.1) fails a test run instead of only surfacing live.
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgis/postgis:16-3.4-alpine")
        .WithDatabase("omyfish")
        .WithUsername("omyfish")
        .WithPassword("omyfish_dev")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var migrationsDir = Path.Combine(FindRepoRoot(), "migrations", "ObservationService");
        var migrator = DeployChanges.To
            .PostgresqlDatabase(ConnectionString)
            .WithScriptsFromFileSystem(migrationsDir)
            .LogToConsole()
            .Build();
        var result = migrator.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException("Test database migration failed.", result.Error);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public ObservationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ObservationDbContext>().UseNpgsql(ConnectionString).Options);

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "omyfish-dotnet.slnx")))
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        return dir ?? throw new InvalidOperationException(
            $"Could not locate repo root (omyfish-dotnet.slnx) from {AppContext.BaseDirectory}");
    }
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
