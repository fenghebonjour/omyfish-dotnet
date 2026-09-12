using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using OMyFish.SpeciesService.Application.Interfaces;
using OMyFish.SpeciesService.Domain.Entities;
using OMyFish.SpeciesService.Infrastructure.Persistence;

namespace OMyFish.SpeciesService.Infrastructure.Repositories;

// Species catalog persistence moved to MongoDB (BACKLOG.md item E) — read-mostly,
// flexible-schema reference data with no relational integrity needs, so it doesn't belong on
// Postgres. Predictions stay EF/Postgres-backed (PredictionRepository) since they're written
// in the same transaction as the outbox event publish (§2.3), which Mongo can't take part in.
public class SpeciesRepository : ISpeciesRepository
{
    private readonly IMongoCollection<SpeciesDocument> _collection;

    public SpeciesRepository(IMongoDatabase database) =>
        _collection = database.GetCollection<SpeciesDocument>("species");

    public async Task<Species?> FindByScientificNameAsync(string scientificName, CancellationToken ct = default)
    {
        var doc = await _collection.Find(CaseInsensitiveEquals(scientificName)).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<IReadOnlyList<Species>> FindByScientificNamesAsync(
        IEnumerable<string> scientificNames, CancellationToken ct = default)
    {
        var names = scientificNames.ToList();
        if (names.Count == 0) return [];

        var filter = Builders<SpeciesDocument>.Filter.Or(names.Select(CaseInsensitiveEquals));
        var docs = await _collection.Find(filter).ToListAsync(ct);
        return docs.Select(d => d.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<Species>> GetAllAsync(CancellationToken ct = default)
    {
        var docs = await _collection.Find(FilterDefinition<SpeciesDocument>.Empty).ToListAsync(ct);
        return docs.Select(d => d.ToDomain()).ToList();
    }

    public Task AddAsync(Species species, CancellationToken ct = default) =>
        _collection.InsertOneAsync(SpeciesDocument.FromDomain(species), cancellationToken: ct);

    public async Task AddIfNotExistsAsync(Species species, CancellationToken ct = default)
    {
        var exists = await _collection.Find(
            Builders<SpeciesDocument>.Filter.Eq(d => d.ScientificName, species.ScientificName))
            .AnyAsync(ct);
        if (exists) return;
        await _collection.InsertOneAsync(SpeciesDocument.FromDomain(species), cancellationToken: ct);
    }

    // Matches the old EF-backed repository's .ToLower() == .ToLower() comparison.
    private static FilterDefinition<SpeciesDocument> CaseInsensitiveEquals(string scientificName) =>
        Builders<SpeciesDocument>.Filter.Regex(
            d => d.ScientificName,
            new BsonRegularExpression($"^{Regex.Escape(scientificName)}$", "i"));
}
