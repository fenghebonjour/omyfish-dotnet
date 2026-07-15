namespace OMyFish.SpeciesService.Application.DTOs;

// Mirrors the ai-service /bite-score response. The per-factor breakdown is a
// product invariant — always pass it through to clients, never reduce a
// forecast to just the headline score.
public sealed record BiteHourlyScoreDto(
    DateTime Timestamp,
    double Score,
    IReadOnlyDictionary<string, double> Breakdown,
    IReadOnlyDictionary<string, double> WeightedContribution,
    double TimeOfDayMultiplier,
    string? SafetyFlag);

public sealed record BiteForecastDto(
    string Species,
    double Lat,
    double Lon,
    IReadOnlyList<BiteHourlyScoreDto> Hourly,
    IReadOnlyList<BiteHourlyScoreDto> BestWindows);
