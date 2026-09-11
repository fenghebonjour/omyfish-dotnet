using Microsoft.EntityFrameworkCore;
using OMyFish.SpeciesService.Domain.Entities;
using OMyFish.SpeciesService.Domain.ValueObjects;
using OMyFish.SpeciesService.Infrastructure.Persistence;
using OMyFish.SpeciesService.Infrastructure.Repositories;
using Xunit;

namespace OMyFish.SpeciesService.Tests;

// Exercises SpeciesRepository/UnitOfWork against a real, migrated Postgres — this is the exact
// path that 500'd in production-shaped testing with `23502: null value in column
// "scientific_name" of relation "predictions"` before that column was mapped
// (BACKLOG.md item F §2.3). A unit test with a mocked repository can't catch a schema/entity
// mismatch like that; only a real database round-trip can.
[Collection("Postgres")]
public class SpeciesRepositoryIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AddAsync_NewSpeciesWithPrediction_PersistsBothInOneTransaction()
    {
        var scientificName = $"Test.species.{Guid.NewGuid():N}";
        var species = Species.Create(scientificName, "Test Fish", "Testidae",
            "LC", "Test habitat", "Test range", "A fish used only in tests.", true);
        var prediction = species.IdentifyFrom("identify/test/image.jpg", ConfidenceScore.Create(0.87), rank: 1);

        await using (var db = fixture.CreateDbContext())
        {
            var repo = new SpeciesRepository(db);
            var unitOfWork = new UnitOfWork(db);

            await repo.AddAsync(species);
            await repo.AddPredictionAsync(prediction);
            await unitOfWork.SaveChangesAsync();
        }

        await using var verifyDb = fixture.CreateDbContext();
        var savedSpecies = await verifyDb.Species.SingleAsync(s => s.ScientificName == scientificName);
        Assert.Equal("Test Fish", savedSpecies.CommonName);

        var savedPrediction = await verifyDb.Predictions.SingleAsync(p => p.Id == prediction.Id);
        Assert.Equal(scientificName, savedPrediction.ScientificName);
        Assert.Equal("identify/test/image.jpg", savedPrediction.ImageStorageKey);
        Assert.Equal(1, savedPrediction.Rank);
    }

    [Fact]
    public async Task AddPredictionAsync_ForAlreadyKnownSpecies_DoesNotDuplicateTheSpeciesRow()
    {
        var scientificName = $"Test.species.{Guid.NewGuid():N}";
        var species = Species.Create(scientificName, "Repeat Fish", "Testidae",
            "LC", "Test habitat", "Test range", "", false);

        await using (var seedDb = fixture.CreateDbContext())
        {
            await new SpeciesRepository(seedDb).AddAsync(species);
            await new UnitOfWork(seedDb).SaveChangesAsync();
        }

        // Simulates IdentifyFishCommandHandler's "already in the catalog" path: the species
        // came back from FindByScientificNamesAsync (already tracked), so only the prediction
        // is staged — AddAsync is deliberately not called a second time.
        await using (var db = fixture.CreateDbContext())
        {
            var existing = await db.Species.SingleAsync(s => s.ScientificName == scientificName);
            var prediction = existing.IdentifyFrom("identify/test/second.jpg", ConfidenceScore.Create(0.5), rank: 1);

            await new SpeciesRepository(db).AddPredictionAsync(prediction);
            await new UnitOfWork(db).SaveChangesAsync();
        }

        await using var verifyDb = fixture.CreateDbContext();
        var speciesCount = await verifyDb.Species.CountAsync(s => s.ScientificName == scientificName);
        Assert.Equal(1, speciesCount);
    }
}
