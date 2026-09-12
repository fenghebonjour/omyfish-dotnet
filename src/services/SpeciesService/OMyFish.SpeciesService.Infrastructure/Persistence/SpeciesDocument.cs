using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using OMyFish.SpeciesService.Domain.Entities;

namespace OMyFish.SpeciesService.Infrastructure.Persistence;

// MongoDB wire format for Species (BACKLOG.md item E) — kept as an explicit document + mapping
// pair, rather than mapping the domain type directly, so the id-restoration path stays simple
// and auditable. Mirrors omyfish-java's SpeciesDocument/toDomain()/from() pattern.
internal sealed class SpeciesDocument
{
    [BsonId, BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    public string ScientificName { get; set; } = default!;
    public string CommonName { get; set; } = default!;
    public string Family { get; set; } = default!;
    public string ConservationStatus { get; set; } = default!;
    public string Habitat { get; set; } = default!;
    public string GeographicRange { get; set; } = default!;
    public string Description { get; set; } = default!;
    public bool IsNorthAmericanFreshwater { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    public static SpeciesDocument FromDomain(Species species) => new()
    {
        Id = species.Id,
        ScientificName = species.ScientificName,
        CommonName = species.CommonName,
        Family = species.Family,
        ConservationStatus = species.ConservationStatus,
        Habitat = species.Habitat,
        GeographicRange = species.GeographicRange,
        Description = species.Description,
        IsNorthAmericanFreshwater = species.IsNorthAmericanFreshwater,
        ImageUrl = species.ImageUrl,
        CreatedAt = species.CreatedAt,
    };

    // Restores the persisted id via Species.Reconstitute — never Species.Create, which would
    // mint a fresh random id on every read. This is the one spot in the whole migration that
    // could reintroduce the exact bug item E calls out in omyfish-java's equivalent mapper, so
    // it's covered directly by SpeciesMongoRepositoryTests (round-trip id equality).
    public Species ToDomain() => Species.Reconstitute(
        Id, ScientificName, CommonName, Family, ConservationStatus, Habitat,
        GeographicRange, Description, IsNorthAmericanFreshwater, ImageUrl, CreatedAt);
}
