using Microsoft.EntityFrameworkCore;
using Npgsql;
using OMyFish.ObservationService.Domain.Entities;
using OMyFish.ObservationService.Domain.ValueObjects;
using OMyFish.ObservationService.Infrastructure.Persistence;
using OMyFish.ObservationService.Infrastructure.Repositories;
using Xunit;

namespace OMyFish.ObservationService.Tests;

// Exercises ObservationRepository/UnitOfWork against a real, migrated Postgres+PostGIS — the
// outbox wiring in CreateObservationCommandHandler (BACKLOG.md item F §2.3) depends on AddAsync
// only staging the entity and UnitOfWork.SaveChangesAsync being what actually commits it; a
// mocked repository can't catch that contract breaking.
[Collection("Postgres")]
public class ObservationRepositoryIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AddAsync_ThenUnitOfWorkSaveChanges_PersistsTheObservation()
    {
        var userId = Guid.NewGuid();
        var location = GpsCoordinates.Create(46.8, -71.2);
        var observation = Observation.Create(
            userId, "Arctic Char", "Salvelinus alpinus", 0.9,
            "identify/test/image.jpg", location, exifMetadata: null, notes: "integration test");

        await using (var db = fixture.CreateDbContext())
        {
            var repo = new ObservationRepository(db);
            var unitOfWork = new UnitOfWork(db);

            await repo.AddAsync(observation);
            await unitOfWork.SaveChangesAsync();
        }

        await using var verifyDb = fixture.CreateDbContext();
        var saved = await verifyDb.Observations.SingleAsync(o => o.Id == observation.Id);
        Assert.Equal(userId, saved.UserId);
        Assert.Equal("Arctic Char", saved.SpeciesName);
        Assert.Equal(46.8, saved.Latitude);
        Assert.Equal(-71.2, saved.Longitude);
    }

    [Fact]
    public async Task AddAsync_WithCoordinates_PostGisLocationTriggerPopulatesLocationColumn()
    {
        // Regression coverage for BACKLOG.md item F §3.3: the `location` GEOMETRY column is
        // derived by a DB trigger, not by EF (ObservationDbContext ignores it) — this proves
        // the trigger actually fires on a real insert through the repository, not just that
        // the migration file parses.
        var observation = Observation.Create(
            Guid.NewGuid(), "Walleye", "Sander vitreus", 0.8,
            "identify/test/walleye.jpg", GpsCoordinates.Create(45.5, -73.5),
            exifMetadata: null, notes: null);

        await using (var db = fixture.CreateDbContext())
        {
            await new ObservationRepository(db).AddAsync(observation);
            await new UnitOfWork(db).SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT location IS NOT NULL FROM observations WHERE id = @id", connection);
        cmd.Parameters.AddWithValue("id", observation.Id);
        var locationIsSet = (bool)(await cmd.ExecuteScalarAsync())!;

        Assert.True(locationIsSet);
    }
}
