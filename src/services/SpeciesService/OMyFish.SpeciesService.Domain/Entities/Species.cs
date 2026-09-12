using OMyFish.Shared.BuildingBlocks.Domain;
using OMyFish.SpeciesService.Domain.Events;
using OMyFish.SpeciesService.Domain.ValueObjects;

namespace OMyFish.SpeciesService.Domain.Entities;

public sealed class Species : AggregateRoot<Guid>
{
    private Species(Guid id) : base(id) { }

    public string ScientificName { get; private set; } = default!;
    public string CommonName { get; private set; } = default!;
    public string Family { get; private set; } = default!;
    public string ConservationStatus { get; private set; } = default!;
    public string Habitat { get; private set; } = default!;
    public string GeographicRange { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public bool IsNorthAmericanFreshwater { get; private set; }
    public string? ImageUrl { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static Species Create(
        string scientificName,
        string commonName,
        string family,
        string conservationStatus,
        string habitat,
        string geographicRange,
        string description,
        bool isNorthAmericanFreshwater,
        string? imageUrl = null)
    {
        var species = new Species(Guid.NewGuid())
        {
            ScientificName = scientificName,
            CommonName = commonName,
            Family = family,
            ConservationStatus = conservationStatus,
            Habitat = habitat,
            GeographicRange = geographicRange,
            Description = description,
            IsNorthAmericanFreshwater = isNorthAmericanFreshwater,
            ImageUrl = imageUrl,
            CreatedAt = DateTime.UtcNow
        };
        return species;
    }

    // Restores a previously-persisted species with its original id — used by the MongoDB
    // repository's document-to-domain mapping (BACKLOG.md item E). Never call this with a
    // freshly-generated id; use Create for that. Mirrors omyfish-java's Species.reconstitute(),
    // which exists specifically because a toDomain() that calls create() instead mints a new
    // random id on every read — the exact bug this separate factory method rules out.
    public static Species Reconstitute(
        Guid id,
        string scientificName,
        string commonName,
        string family,
        string conservationStatus,
        string habitat,
        string geographicRange,
        string description,
        bool isNorthAmericanFreshwater,
        string? imageUrl,
        DateTime createdAt)
    {
        return new Species(id)
        {
            ScientificName = scientificName,
            CommonName = commonName,
            Family = family,
            ConservationStatus = conservationStatus,
            Habitat = habitat,
            GeographicRange = geographicRange,
            Description = description,
            IsNorthAmericanFreshwater = isNorthAmericanFreshwater,
            ImageUrl = imageUrl,
            CreatedAt = createdAt
        };
    }

    public Prediction IdentifyFrom(string imageStorageKey, ConfidenceScore confidence, int rank = 1)
    {
        var prediction = Prediction.Create(this, imageStorageKey, confidence, rank);
        RegisterEvent(new FishIdentifiedEvent(prediction.Id, null, Guid.Empty,
            CommonName, confidence.Value,
            [new(CommonName, ScientificName, confidence.Value, rank)],
            imageStorageKey));
        return prediction;
    }
}
