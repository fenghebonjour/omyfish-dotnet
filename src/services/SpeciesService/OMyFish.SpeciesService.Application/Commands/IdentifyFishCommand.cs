using MediatR;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.SpeciesService.Application.Commands;

public sealed record IdentifyFishCommand(
    byte[] ImageBytes,
    string ImageFileName,
    string ImageContentType,
    int TopK,
    Guid UserId,
    Guid? ObservationId = null) : ICommand<IdentifyFishResult>;

public sealed record IdentifyFishResult(
    IReadOnlyList<PredictionDto> Predictions,
    bool Uncertain,
    string ImageKey,
    bool IsFish = true);

public sealed record PredictionDto(
    string SpeciesName,
    string ScientificName,
    double Confidence,
    string ConfidencePercent,
    int Rank,
    string? ConservationStatus,
    string? Habitat = null,
    string? Diet = null,
    int? MaxSizeCm = null,
    string? Description = null,
    string? FunFact = null);
