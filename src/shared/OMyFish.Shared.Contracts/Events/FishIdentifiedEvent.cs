namespace OMyFish.Shared.Contracts.Events;

public record FishIdentifiedEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid PredictionId,
    Guid? ObservationId,
    Guid UserId,
    string TopSpeciesName,
    double TopConfidence,
    IReadOnlyList<PredictionResult> Predictions,
    string ImageStorageKey)
{
    public FishIdentifiedEvent(
        Guid predictionId,
        Guid? observationId,
        Guid userId,
        string topSpeciesName,
        double topConfidence,
        IReadOnlyList<PredictionResult> predictions,
        string imageStorageKey)
        : this(Guid.NewGuid(), DateTime.UtcNow, predictionId, observationId,
               userId, topSpeciesName, topConfidence, predictions, imageStorageKey) { }

    public const string RoutingKey = "fish.identified";
    public const string Exchange = "omyfish.species";
}

public record PredictionResult(string SpeciesName, string ScientificName, double Confidence, int Rank);
