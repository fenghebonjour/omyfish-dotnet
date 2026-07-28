using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using OMyFish.SpeciesService.Application.DTOs;
using OMyFish.SpeciesService.Application.Interfaces;

namespace OMyFish.SpeciesService.Infrastructure.ExternalServices;

public class AIServiceClient : IAIServiceClient
{
    private readonly HttpClient _http;

    public AIServiceClient(HttpClient http) => _http = http;

    public async Task<AIServiceResult> PredictAsync(
        string imageBase64, int topK, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/predict",
            new { image_base64 = imageBase64, top_k = topK }, ct);

        if (!response.IsSuccessStatusCode)
            return new AIServiceResult([]);

        var result = await response.Content.ReadFromJsonAsync<AiServiceResponse>(ct);
        if (result?.Predictions is null) return new AIServiceResult([]);

        var predictions = result.Predictions
            .Select((p, i) => new AIPrediction(
                p.ScientificName, p.CommonName, p.Confidence, i + 1,
                p.ConservationStatus, p.Habitat, p.Diet, p.MaxSizeCm, p.Description, p.FunFact))
            .ToList();
        return new AIServiceResult(predictions, result.IsFish);
    }

    public async Task<BiteForecastDto> GetBiteForecastAsync(
        double lat, double lon, string species, int hours, CancellationToken ct = default)
    {
        // Resolve first so callers can pass a confirmed fish-ID name directly;
        // unknown species fall back to the "general" profile instead of a 400.
        var keyResp = await _http.GetFromJsonAsync<SpeciesKeyResponse>(
            $"/bite-score/species-key?name={Uri.EscapeDataString(species)}", ct);
        var speciesKey = keyResp?.SpeciesKey ?? "general";

        var lc = CultureInfo.InvariantCulture;
        var forecast = await _http.GetFromJsonAsync<BiteForecastResponse>(
            $"/bite-score/forecast?lat={lat.ToString(lc)}&lon={lon.ToString(lc)}&species={speciesKey}&hours={hours}", ct)
            ?? throw new HttpRequestException("Empty bite-score response from ai-service.");

        return new BiteForecastDto(
            forecast.Species, forecast.Lat, forecast.Lon,
            forecast.Hourly.Select(ToDto).ToList(),
            forecast.BestWindows.Select(ToDto).ToList(),
            ToWindows(forecast.MajorWindows),
            ToWindows(forecast.MinorWindows),
            ToSunTimes(forecast.SunTimes),
            forecast.Current is null
                ? null
                : new CurrentConditionsDto(
                    forecast.Current.Time, forecast.Current.PrecipitationMm,
                    forecast.Current.IsStorm, forecast.Current.IsHeavyPrecip));
    }

    private static BiteHourlyScoreDto ToDto(BiteHourlyScore h) => new(
        h.Timestamp, h.Score, h.Breakdown, h.WeightedContribution,
        h.TimeOfDayMultiplier, h.SafetyFlag);

    // Null-safe: an ai-service image predating the peak windows omits the fields.
    private static IReadOnlyList<TimeWindowDto> ToWindows(List<TimeWindow>? windows) =>
        windows?.Select(w => new TimeWindowDto(w.Start, w.End)).ToList() ?? [];

    private static IReadOnlyList<SunTimesDto> ToSunTimes(List<SunTimes>? sunTimes) =>
        sunTimes?.Select(s => new SunTimesDto(s.Date, s.Sunrise, s.Sunset)).ToList() ?? [];

    private sealed record SpeciesKeyResponse(
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("species_key")] string SpeciesKey,
        [property: JsonPropertyName("matched")] bool Matched);

    private sealed record BiteForecastResponse(
        [property: JsonPropertyName("species")] string Species,
        [property: JsonPropertyName("lat")] double Lat,
        [property: JsonPropertyName("lon")] double Lon,
        [property: JsonPropertyName("hourly")] List<BiteHourlyScore> Hourly,
        [property: JsonPropertyName("best_windows")] List<BiteHourlyScore> BestWindows,
        [property: JsonPropertyName("major_windows")] List<TimeWindow>? MajorWindows = null,
        [property: JsonPropertyName("minor_windows")] List<TimeWindow>? MinorWindows = null,
        [property: JsonPropertyName("sun_times")] List<SunTimes>? SunTimes = null,
        [property: JsonPropertyName("current")] CurrentConditions? Current = null);

    private sealed record TimeWindow(
        [property: JsonPropertyName("start")] DateTime Start,
        [property: JsonPropertyName("end")] DateTime End);

    private sealed record SunTimes(
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("sunrise")] DateTime Sunrise,
        [property: JsonPropertyName("sunset")] DateTime Sunset);

    private sealed record CurrentConditions(
        [property: JsonPropertyName("time")] DateTime Time,
        [property: JsonPropertyName("precipitation_mm")] double PrecipitationMm,
        [property: JsonPropertyName("is_storm")] bool IsStorm,
        [property: JsonPropertyName("is_heavy_precip")] bool IsHeavyPrecip);

    private sealed record BiteHourlyScore(
        [property: JsonPropertyName("timestamp")] DateTime Timestamp,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("breakdown")] Dictionary<string, double> Breakdown,
        [property: JsonPropertyName("weighted_contribution")] Dictionary<string, double> WeightedContribution,
        [property: JsonPropertyName("time_of_day_multiplier")] double TimeOfDayMultiplier,
        [property: JsonPropertyName("safety_flag")] string? SafetyFlag);

    private sealed record AiServiceResponse(
        [property: JsonPropertyName("predictions")] List<AiPrediction> Predictions,
        [property: JsonPropertyName("uncertain")] bool Uncertain,
        [property: JsonPropertyName("is_fish")] bool IsFish = true);

    private sealed record AiPrediction(
        [property: JsonPropertyName("scientific_name")] string ScientificName,
        [property: JsonPropertyName("common_name")] string CommonName,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("conservation_status")] string? ConservationStatus = null,
        [property: JsonPropertyName("habitat")] string? Habitat = null,
        [property: JsonPropertyName("diet")] string? Diet = null,
        [property: JsonPropertyName("max_size_cm")] int? MaxSizeCm = null,
        [property: JsonPropertyName("description")] string? Description = null,
        [property: JsonPropertyName("fun_fact")] string? FunFact = null);
}
