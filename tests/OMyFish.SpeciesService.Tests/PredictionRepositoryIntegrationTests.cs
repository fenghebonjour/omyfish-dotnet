using Microsoft.EntityFrameworkCore;
using OMyFish.SpeciesService.Domain.Entities;
using OMyFish.SpeciesService.Domain.ValueObjects;
using OMyFish.SpeciesService.Infrastructure.Repositories;
using Xunit;

namespace OMyFish.SpeciesService.Tests;

// Exercises PredictionRepository/UnitOfWork against a real, migrated Postgres. Predictions
// stay here (rather than moving to MongoDB with the species catalog) specifically because they
// commit in the same transaction as the outbox message on every /identify call (BACKLOG.md
// item F §2.3) — this is what proves that still works once the species_id FK/column is gone
// (migration 003, BACKLOG.md item E).
[Collection("Postgres")]
public class PredictionRepositoryIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AddPredictionAsync_ThenSaveChanges_PersistsTheRow()
    {
        var scientificName = $"Test.species.{Guid.NewGuid():N}";
        var species = Species.Create(scientificName, "Test Fish", "Testidae",
            "LC", "Test habitat", "Test range", "A fish used only in tests.", true);
        var prediction = species.IdentifyFrom("identify/test/image.jpg", ConfidenceScore.Create(0.87), rank: 1);

        await using (var db = fixture.CreateDbContext())
        {
            await new PredictionRepository(db).AddPredictionAsync(prediction);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = fixture.CreateDbContext();
        var saved = await verifyDb.Predictions.SingleAsync(p => p.Id == prediction.Id);
        Assert.Equal(scientificName, saved.ScientificName);
        Assert.Equal("identify/test/image.jpg", saved.ImageStorageKey);
        Assert.Equal(1, saved.Rank);
    }
}
