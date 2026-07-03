using System.Net.Http.Json;
using System.Text.Json.Serialization;
using OMyFish.SpeciesService.Application.Interfaces;

namespace OMyFish.SpeciesService.Infrastructure.ExternalServices;

// Phase 4: imageStorageKey carries base64 image data directly.
// Phase 5 will upload to MinIO first and pass the real storage key.
public class AIServiceClient : IAIServiceClient
{
    private readonly HttpClient _http;

    public AIServiceClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<AIPrediction>> PredictAsync(
        string imageBase64, int topK, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/predict",
            new { image_base64 = imageBase64, top_k = topK }, ct);

        if (!response.IsSuccessStatusCode)
            return [];

        var result = await response.Content.ReadFromJsonAsync<AiServiceResponse>(ct);
        if (result?.Predictions is null) return [];

        return result.Predictions
            .Select((p, i) => new AIPrediction(
                p.ScientificName, p.CommonName, p.Confidence, i + 1,
                p.ConservationStatus, p.Habitat, p.Diet, p.MaxSizeCm, p.Description, p.FunFact))
            .ToList();
    }

    private sealed record AiServiceResponse(
        [property: JsonPropertyName("predictions")] List<AiPrediction> Predictions,
        [property: JsonPropertyName("uncertain")] bool Uncertain);

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
