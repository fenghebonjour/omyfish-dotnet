using OMyFish.Shared.BuildingBlocks.Domain;

namespace OMyFish.ObservationService.Domain.Events;

public sealed record ObservationCreatedEvent : DomainEvent
{
    public Guid ObservationId { get; init; }
    public Guid UserId { get; init; }
    public string SpeciesName { get; init; } = default!;
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string ImageStorageKey { get; init; } = default!;
    public DateTime ObservedAt { get; init; }

    public ObservationCreatedEvent(
        Guid observationId, Guid userId, string speciesName,
        double? latitude, double? longitude, string imageStorageKey, DateTime observedAt)
        : base("observation.created")
    {
        ObservationId = observationId;
        UserId = userId;
        SpeciesName = speciesName;
        Latitude = latitude;
        Longitude = longitude;
        ImageStorageKey = imageStorageKey;
        ObservedAt = observedAt;
    }
}
