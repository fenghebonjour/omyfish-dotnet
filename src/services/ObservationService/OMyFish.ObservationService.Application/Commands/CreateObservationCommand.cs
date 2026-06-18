using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ObservationService.Application.Commands;

public sealed record CreateObservationCommand(
    Guid UserId,
    string SpeciesName,
    string? ScientificName,
    double TopConfidence,
    Stream ImageStream,
    string ImageFileName,
    string ImageContentType,
    double? Latitude,
    double? Longitude,
    string? Notes) : ICommand<CreateObservationResult>;

public sealed record CreateObservationResult(
    Guid ObservationId,
    string SpeciesName,
    string ImageStorageKey,
    DateTime ObservedAt);
