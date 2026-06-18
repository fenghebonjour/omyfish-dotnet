namespace OMyFish.Shared.Contracts.Events;

public record ObservationCreatedEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid ObservationId,
    Guid UserId,
    string SpeciesName,
    double? Latitude,
    double? Longitude,
    string ImageStorageKey,
    DateTime ObservedAt)
{
    public ObservationCreatedEvent(
        Guid observationId, Guid userId, string speciesName,
        double? latitude, double? longitude, string imageStorageKey, DateTime observedAt)
        : this(Guid.NewGuid(), DateTime.UtcNow, observationId, userId,
               speciesName, latitude, longitude, imageStorageKey, observedAt) { }

    public const string RoutingKey = "observation.created";
    public const string Exchange = "omyfish.observations";
}
