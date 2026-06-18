using OMyFish.ObservationService.Domain.Events;
using OMyFish.ObservationService.Domain.ValueObjects;
using OMyFish.Shared.BuildingBlocks.Domain;

namespace OMyFish.ObservationService.Domain.Entities;

public sealed class Observation : AggregateRoot<Guid>
{
    private Observation(Guid id) : base(id) { }

    public Guid UserId { get; private set; }
    public string SpeciesName { get; private set; } = default!;
    public string? ScientificName { get; private set; }
    public double TopConfidence { get; private set; }
    public string ImageStorageKey { get; private set; } = default!;
    public GpsCoordinates? Location { get; private set; }
    public ExifMetadata? ExifMetadata { get; private set; }
    public string? Notes { get; private set; }
    public DateTime ObservedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static Observation Create(
        Guid userId,
        string speciesName,
        string? scientificName,
        double topConfidence,
        string imageStorageKey,
        GpsCoordinates? location,
        ExifMetadata? exifMetadata,
        string? notes)
    {
        var obs = new Observation(Guid.NewGuid())
        {
            UserId = userId,
            SpeciesName = speciesName,
            ScientificName = scientificName,
            TopConfidence = topConfidence,
            ImageStorageKey = imageStorageKey,
            Location = location,
            ExifMetadata = exifMetadata,
            Notes = notes,
            ObservedAt = exifMetadata?.CapturedAt ?? DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        obs.RegisterEvent(new ObservationCreatedEvent(
            obs.Id, userId, speciesName,
            location?.Latitude, location?.Longitude,
            imageStorageKey, obs.ObservedAt));

        return obs;
    }
}
