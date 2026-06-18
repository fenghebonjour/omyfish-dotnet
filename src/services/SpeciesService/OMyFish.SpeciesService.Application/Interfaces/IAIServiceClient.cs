namespace OMyFish.SpeciesService.Application.Interfaces;

public interface IAIServiceClient
{
    Task<IReadOnlyList<AIPrediction>> PredictAsync(
        string imageStorageKey, int topK, CancellationToken ct = default);
}

public sealed record AIPrediction(
    string ScientificName,
    string CommonName,
    double Confidence,
    int Rank);
