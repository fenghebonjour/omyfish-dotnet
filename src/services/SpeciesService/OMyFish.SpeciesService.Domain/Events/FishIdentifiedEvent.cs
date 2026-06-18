using OMyFish.Shared.BuildingBlocks.Domain;

namespace OMyFish.SpeciesService.Domain.Events;

public sealed record FishIdentifiedEvent : DomainEvent
{
    public Guid PredictionId { get; init; }
    public Guid? ObservationId { get; init; }
    public Guid UserId { get; init; }
    public string TopSpeciesName { get; init; } = default!;
    public double TopConfidence { get; init; }
    public IReadOnlyList<PredictionItem> Predictions { get; init; } = [];
    public string ImageStorageKey { get; init; } = default!;

    public FishIdentifiedEvent(
        Guid predictionId, Guid? observationId, Guid userId,
        string topSpeciesName, double topConfidence,
        IReadOnlyList<PredictionItem> predictions, string imageStorageKey)
        : base("fish.identified")
    {
        PredictionId = predictionId;
        ObservationId = observationId;
        UserId = userId;
        TopSpeciesName = topSpeciesName;
        TopConfidence = topConfidence;
        Predictions = predictions;
        ImageStorageKey = imageStorageKey;
    }
}

public record PredictionItem(string SpeciesName, string ScientificName, double Confidence, int Rank);
