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

public sealed record TimeWindowDto(
    DateTime Start,
    DateTime End);

// Date stays an ISO date string ("2026-07-16") — clients match it against
// hourly timestamps by day.
public sealed record SunTimesDto(
    string Date,
    DateTime Sunrise,
    DateTime Sunset);

public sealed record CurrentConditionsDto(
    DateTime Time,
    double PrecipitationMm,
    bool IsStorm,
    bool IsHeavyPrecip);

public sealed record BiteForecastDto(
    string Species,
    double Lat,
    double Lon,
    IReadOnlyList<BiteHourlyScoreDto> Hourly,
    IReadOnlyList<BiteHourlyScoreDto> BestWindows,
    IReadOnlyList<TimeWindowDto> MajorWindows,
    IReadOnlyList<TimeWindowDto> MinorWindows,
    IReadOnlyList<SunTimesDto> SunTimes,
    CurrentConditionsDto? Current);
