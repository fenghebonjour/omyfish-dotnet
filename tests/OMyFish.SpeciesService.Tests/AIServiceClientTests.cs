using System.Net;
using System.Text;
using OMyFish.SpeciesService.Infrastructure.ExternalServices;
using Xunit;

namespace OMyFish.SpeciesService.Tests;

// AIServiceClient had zero test coverage (BACKLOG.md item F, Testing/CI tier) despite being
// the adapter every identify/bite-score/regs call goes through. Exercises it against a fake
// HttpMessageHandler so the JSON-mapping and defensive-fallback logic is verified without a
// real ai-service dependency.
public class AIServiceClientTests
{
    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static AIServiceClient ClientWith(params HttpResponseMessage[] responses)
    {
        var handler = new FakeHttpMessageHandler(responses);
        return new AIServiceClient(new HttpClient(handler) { BaseAddress = new Uri("http://ai-service/") });
    }

    [Fact]
    public async Task PredictAsync_Success_MapsPredictionsInOrderWithSequentialRank()
    {
        var client = ClientWith(JsonResponse(HttpStatusCode.OK, """
            {
              "predictions": [
                {"scientific_name": "Esox lucius", "common_name": "Northern Pike", "confidence": 0.92,
                 "conservation_status": "LC", "habitat": "Lakes", "diet": "Carnivore",
                 "max_size_cm": 150, "description": "desc", "fun_fact": "fact"},
                {"scientific_name": "Sander vitreus", "common_name": "Walleye", "confidence": 0.05}
              ],
              "uncertain": false,
              "is_fish": true
            }
            """));

        var result = await client.PredictAsync("base64img", topK: 2);

        Assert.True(result.IsFish);
        Assert.Equal(2, result.Predictions.Count);
        Assert.Equal(1, result.Predictions[0].Rank);
        Assert.Equal("Esox lucius", result.Predictions[0].ScientificName);
        Assert.Equal(150, result.Predictions[0].MaxSizeCm);
        Assert.Equal(2, result.Predictions[1].Rank);
        Assert.Equal("Sander vitreus", result.Predictions[1].ScientificName);
    }

    [Fact]
    public async Task PredictAsync_NonSuccessStatusCode_ReturnsEmptyResultInsteadOfThrowing()
    {
        var client = ClientWith(JsonResponse(HttpStatusCode.InternalServerError, "{}"));

        var result = await client.PredictAsync("base64img", topK: 3);

        Assert.Empty(result.Predictions);
    }

    [Fact]
    public async Task PredictAsync_NullPredictionsInBody_ReturnsEmptyResult()
    {
        var client = ClientWith(JsonResponse(HttpStatusCode.OK, """{"uncertain": true}"""));

        var result = await client.PredictAsync("base64img", topK: 3);

        Assert.Empty(result.Predictions);
    }

    [Fact]
    public async Task GetBiteForecastAsync_PassesSixFactorBreakdownThroughUntouched()
    {
        var client = ClientWith(
            JsonResponse(HttpStatusCode.OK, """{"input": "walleye", "species_key": "walleye", "matched": true}"""),
            JsonResponse(HttpStatusCode.OK, """
                {
                  "species": "walleye",
                  "lat": 45.5,
                  "lon": -73.5,
                  "hourly": [
                    {"timestamp": "2026-07-16T06:00:00Z", "score": 0.81,
                     "breakdown": {"moon": 0.2, "pressure": 0.1, "wind": 0.15, "time_of_day": 0.2, "season": 0.1, "temperature": 0.06},
                     "weighted_contribution": {"moon": 0.1, "pressure": 0.05, "wind": 0.08, "time_of_day": 0.1, "season": 0.05, "temperature": 0.03},
                     "time_of_day_multiplier": 1.2, "safety_flag": null}
                  ],
                  "best_windows": []
                }
                """));

        var forecast = await client.GetBiteForecastAsync(45.5, -73.5, "Sander vitreus", hours: 24);

        var breakdown = forecast.Hourly[0].Breakdown;
        Assert.Equal(6, breakdown.Count);
        Assert.Equal(0.2, breakdown["moon"]);
        Assert.Equal(0.06, breakdown["temperature"]);
    }

    [Fact]
    public async Task GetBiteForecastAsync_UnresolvedSpeciesKey_FallsBackToGeneralProfile()
    {
        var client = ClientWith(
            JsonResponse(HttpStatusCode.OK, "null"),
            JsonResponse(HttpStatusCode.OK, """
                {"species": "general", "lat": 45.5, "lon": -73.5, "hourly": [], "best_windows": []}
                """));

        var forecast = await client.GetBiteForecastAsync(45.5, -73.5, "Some Unknown Fish", hours: 24);

        Assert.Equal("general", forecast.Species);
    }

    [Fact]
    public async Task GetBiteForecastAsync_NullForecastBody_ThrowsHttpRequestException()
    {
        var client = ClientWith(
            JsonResponse(HttpStatusCode.OK, """{"input": "x", "species_key": "walleye", "matched": true}"""),
            JsonResponse(HttpStatusCode.OK, "null"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetBiteForecastAsync(45.5, -73.5, "walleye", hours: 24));
    }

    [Fact]
    public async Task AskRegsAsync_NullResponseBody_ThrowsHttpRequestException()
    {
        var client = ClientWith(JsonResponse(HttpStatusCode.OK, "null"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.AskRegsAsync("Can I keep a walleye under 30cm?"));
    }

    [Fact]
    public async Task GetRegsZonesGeoJsonAsync_NullResponseBody_ReturnsEmptyDictionary()
    {
        var client = ClientWith(JsonResponse(HttpStatusCode.OK, "null"));

        var geoJson = await client.GetRegsZonesGeoJsonAsync();

        Assert.Empty(geoJson);
    }
}

internal sealed class FakeHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new(responses);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_responses.Count == 0)
            throw new InvalidOperationException($"No more canned responses for {request.RequestUri}.");
        return Task.FromResult(_responses.Dequeue());
    }
}
