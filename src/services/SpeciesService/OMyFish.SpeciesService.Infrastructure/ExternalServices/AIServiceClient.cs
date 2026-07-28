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

    public async Task<RegsLimitsDto> GetRegsLimitsAsync(
        double lat, double lon, string species, CancellationToken ct = default)
    {
        var lc = CultureInfo.InvariantCulture;
        var dto = await _http.GetFromJsonAsync<RegsLimitsResponse>(
            $"/regs/limits?lat={lat.ToString(lc)}&lon={lon.ToString(lc)}&species={Uri.EscapeDataString(species)}", ct)
            ?? throw new HttpRequestException("Empty regs limits response from ai-service.");

        return new RegsLimitsDto(
            dto.Lat, dto.Lon, dto.ZoneName, dto.ZoneInfoUrl,
            dto.Rules.Select(r => new RegsSpeciesLimitDto(
                r.Species, r.Period, r.CatchLimit, r.LengthLimit, r.FishingDevice, r.Note)).ToList(),
            dto.Disclaimer);
    }

    public async Task<IReadOnlyDictionary<string, object>> GetRegsZonesGeoJsonAsync(CancellationToken ct = default)
    {
        var geoJson = await _http.GetFromJsonAsync<Dictionary<string, object>>("/regs/zones/geojson", ct);
        return geoJson ?? new Dictionary<string, object>();
    }

    public async Task<IReadOnlyList<RegsStationDto>> GetRegsConsumptionStationsAsync(
        double lat, double lon, int limit, CancellationToken ct = default)
    {
        var lc = CultureInfo.InvariantCulture;
        var stations = await _http.GetFromJsonAsync<List<RegsStationResponse>>(
            $"/regs/consumption/stations?lat={lat.ToString(lc)}&lon={lon.ToString(lc)}&limit={limit}", ct);
        return stations?.Select(s => new RegsStationDto(
            s.NoBqma, s.Hydronyme, s.Latitude, s.Longitude, s.DistanceKm)).ToList() ?? [];
    }

    public async Task<RegsConsumptionDto> GetRegsConsumptionAsync(
        double lat, double lon, string species, double? sizeCm, CancellationToken ct = default)
    {
        var lc = CultureInfo.InvariantCulture;
        var query = $"/regs/consumption?lat={lat.ToString(lc)}&lon={lon.ToString(lc)}&species={Uri.EscapeDataString(species)}";
        if (sizeCm.HasValue) query += $"&size_cm={sizeCm.Value.ToString(lc)}";

        var dto = await _http.GetFromJsonAsync<RegsConsumptionResponse>(query, ct)
            ?? throw new HttpRequestException("Empty regs consumption response from ai-service.");

        return new RegsConsumptionDto(
            dto.Lat, dto.Lon, dto.Species, dto.StationName, dto.DistanceKm,
            dto.SizeClass, dto.MealsPerMonth, dto.FishingStatus, dto.Note, dto.Disclaimer);
    }

    public async Task<RegsAnswerDto> AskRegsAsync(string question, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/regs/ask", new { question }, ct);
        var dto = await response.Content.ReadFromJsonAsync<RegsAskResponse>(ct)
            ?? throw new HttpRequestException("Empty regs ask response from ai-service.");
        return new RegsAnswerDto(dto.Question, dto.Answer, dto.Sources, dto.Disclaimer);
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

    private sealed record RegsSpeciesLimitResponse(
        [property: JsonPropertyName("species")] string Species,
        [property: JsonPropertyName("period")] string Period,
        [property: JsonPropertyName("catch_limit")] string CatchLimit,
        [property: JsonPropertyName("length_limit")] string? LengthLimit,
        [property: JsonPropertyName("fishing_device")] string? FishingDevice,
        [property: JsonPropertyName("note")] string? Note);

    private sealed record RegsLimitsResponse(
        [property: JsonPropertyName("lat")] double Lat,
        [property: JsonPropertyName("lon")] double Lon,
        [property: JsonPropertyName("zone_name")] string ZoneName,
        [property: JsonPropertyName("zone_info_url")] string? ZoneInfoUrl,
        [property: JsonPropertyName("rules")] List<RegsSpeciesLimitResponse> Rules,
        [property: JsonPropertyName("disclaimer")] string Disclaimer);

    private sealed record RegsStationResponse(
        [property: JsonPropertyName("no_bqma")] string NoBqma,
        [property: JsonPropertyName("hydronyme")] string Hydronyme,
        [property: JsonPropertyName("latitude")] double Latitude,
        [property: JsonPropertyName("longitude")] double Longitude,
        [property: JsonPropertyName("distance_km")] double DistanceKm);

    private sealed record RegsConsumptionResponse(
        [property: JsonPropertyName("lat")] double Lat,
        [property: JsonPropertyName("lon")] double Lon,
        [property: JsonPropertyName("species")] string Species,
        [property: JsonPropertyName("station_name")] string StationName,
        [property: JsonPropertyName("distance_km")] double DistanceKm,
        [property: JsonPropertyName("size_class")] string? SizeClass,
        [property: JsonPropertyName("meals_per_month")] int? MealsPerMonth,
        [property: JsonPropertyName("fishing_status")] string? FishingStatus,
        [property: JsonPropertyName("note")] string? Note,
        [property: JsonPropertyName("disclaimer")] string Disclaimer);

    private sealed record RegsAskResponse(
        [property: JsonPropertyName("question")] string Question,
        [property: JsonPropertyName("answer")] string Answer,
        [property: JsonPropertyName("sources")] List<string> Sources,
        [property: JsonPropertyName("disclaimer")] string Disclaimer);
}
