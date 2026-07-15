using OMyFish.SpeciesService.Application.DTOs;

namespace OMyFish.SpeciesService.Application.Interfaces;

public interface IAIServiceClient
{
    Task<AIServiceResult> PredictAsync(
        string imageStorageKey, int topK, CancellationToken ct = default);

    Task<BiteForecastDto> GetBiteForecastAsync(
        double lat, double lon, string species, int hours, CancellationToken ct = default);
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
