using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ObservationService.Application.Queries;

public sealed record GetObservationsQuery(Guid? UserId = null) : IQuery<IReadOnlyList<ObservationDto>>;

public sealed record GetObservationByIdQuery(Guid Id) : IQuery<ObservationDto?>;

public sealed record ObservationDto(
    Guid Id,
    Guid UserId,
    string SpeciesName,
    string? ScientificName,
    double TopConfidence,
    string ImageStorageKey,
    string? ImageUrl,
    double? Latitude,
    double? Longitude,
    string? Notes,
    DateTime ObservedAt,
    DateTime CreatedAt);
