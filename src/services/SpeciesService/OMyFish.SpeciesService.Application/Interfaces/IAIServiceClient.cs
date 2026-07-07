namespace OMyFish.SpeciesService.Application.Interfaces;

public interface IAIServiceClient
{
    Task<AIServiceResult> PredictAsync(
        string imageStorageKey, int topK, CancellationToken ct = default);
}

public sealed record AIServiceResult(
    IReadOnlyList<AIPrediction> Predictions,
    bool IsFish = true);

public sealed record AIPrediction(
    string ScientificName,
    string CommonName,
    double Confidence,
    int Rank,
    string? ConservationStatus = null,
    string? Habitat = null,
    string? Diet = null,
    int? MaxSizeCm = null,
    string? Description = null,
    string? FunFact = null);
