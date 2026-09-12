using MongoDB.Driver;
using OMyFish.SpeciesService.Domain.Entities;
using OMyFish.SpeciesService.Infrastructure.Repositories;
using Testcontainers.MongoDb;
using Xunit;

namespace OMyFish.SpeciesService.Tests;

// Real MongoDB via Testcontainers — exercises the species catalog repository that replaced
// the EF-backed one (BACKLOG.md item E). The id-restoration test specifically rules out
// omyfish-java's equivalent bug, where a toDomain() mapper called create() instead of
// reconstitute() and minted a fresh random id on every read.
public class SpeciesMongoRepositoryTests : IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7").Build();
    private SpeciesRepository _repo = default!;

    public async Task InitializeAsync()
    {
        await _mongo.StartAsync();
        var database = new MongoClient(_mongo.GetConnectionString()).GetDatabase("omyfish");
        _repo = new SpeciesRepository(database);
    }

    public Task DisposeAsync() => _mongo.DisposeAsync().AsTask();

    [Fact]
    public async Task AddAsync_ThenFindByScientificName_RoundTripsTheSpeciesWithItsOriginalId()
    {
        var species = Species.Create("Sander vitreus", "Walleye", "Percidae",
            "LC", "Lake", "North America", "A freshwater fish.", true);

        await _repo.AddAsync(species);
        var found = await _repo.FindByScientificNameAsync("Sander vitreus");

        Assert.NotNull(found);
        // The exact bug this rules out: a mapper that calls Species.Create (minting a new
        // random id) instead of Species.Reconstitute (restoring the persisted one).
        Assert.Equal(species.Id, found!.Id);
        Assert.Equal("Walleye", found.CommonName);
        Assert.True(found.IsNorthAmericanFreshwater);
    }

    [Fact]
    public async Task FindByScientificNameAsync_IsCaseInsensitive()
    {
        await _repo.AddAsync(Species.Create("Esox lucius", "Northern Pike", "Esocidae",
            "LC", "Lake", "North America", "", true));

        var found = await _repo.FindByScientificNameAsync("esox LUCIUS");

        Assert.NotNull(found);
        Assert.Equal("Northern Pike", found!.CommonName);
    }

    [Fact]
    public async Task FindByScientificNamesAsync_BatchLookup_ReturnsOnlyMatches()
    {
        await _repo.AddAsync(Species.Create("Sander vitreus", "Walleye", "Percidae",
            "LC", "Lake", "NA", "", true));
        await _repo.AddAsync(Species.Create("Esox lucius", "Northern Pike", "Esocidae",
            "LC", "Lake", "NA", "", true));

        var found = await _repo.FindByScientificNamesAsync(["Sander vitreus", "Nonexistent species"]);

        var match = Assert.Single(found);
        Assert.Equal("Walleye", match.CommonName);
    }

    [Fact]
    public async Task AddIfNotExistsAsync_ForAlreadyKnownScientificName_DoesNotDuplicate()
    {
        var species = Species.Create("Sander vitreus", "Walleye", "Percidae",
            "LC", "Lake", "NA", "", true);
        await _repo.AddIfNotExistsAsync(species);

        await _repo.AddIfNotExistsAsync(Species.Create(
            "Sander vitreus", "Walleye (dup)", "Percidae", "LC", "Lake", "NA", "", true));

        var all = await _repo.GetAllAsync();
        Assert.Single(all);
    }
}
