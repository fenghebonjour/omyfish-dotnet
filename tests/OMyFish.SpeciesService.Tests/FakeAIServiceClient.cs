using OMyFish.SpeciesService.Application.DTOs;
using OMyFish.SpeciesService.Application.Interfaces;

namespace OMyFish.SpeciesService.Tests;

// Stands in for the real ai-service HTTP dependency in endpoint-level tests — only
// PredictAsync is exercised by the /identify endpoint tests that use this fake.
public sealed class FakeAIServiceClient : IAIServiceClient
{
    public AIServiceResult NextPredictResult { get; set; } = new(
        [new AIPrediction("Esox lucius", "Northern Pike", 0.92, 1, "LC", "Lakes", "Carnivore", 150, "desc", "fact")]);

    public Task<AIServiceResult> PredictAsync(string imageStorageKey, int topK, CancellationToken ct = default)
        => Task.FromResult(NextPredictResult);

    public Task<BiteForecastDto> GetBiteForecastAsync(
        double lat, double lon, string species, int hours, CancellationToken ct = default)
        => throw new NotSupportedException("Not needed by the /identify endpoint tests.");

    public Task<RegsLimitsDto> GetRegsLimitsAsync(
        double lat, double lon, string species, CancellationToken ct = default)
        => throw new NotSupportedException("Not needed by the /identify endpoint tests.");

    public Task<IReadOnlyDictionary<string, object>> GetRegsZonesGeoJsonAsync(CancellationToken ct = default)
        => throw new NotSupportedException("Not needed by the /identify endpoint tests.");

    public Task<IReadOnlyList<RegsStationDto>> GetRegsConsumptionStationsAsync(
        double lat, double lon, int limit, CancellationToken ct = default)
        => throw new NotSupportedException("Not needed by the /identify endpoint tests.");

    public Task<RegsConsumptionDto> GetRegsConsumptionAsync(
        double lat, double lon, string species, double? sizeCm, CancellationToken ct = default)
        => throw new NotSupportedException("Not needed by the /identify endpoint tests.");

    public Task<RegsAnswerDto> AskRegsAsync(string question, CancellationToken ct = default)
        => throw new NotSupportedException("Not needed by the /identify endpoint tests.");
}

// Stands in for the real MinIO dependency — returns a deterministic key without touching
// object storage, matching what species-service's own identify flow expects from IStorageService.
public sealed class FakeStorageService : IStorageService
{
    public Task<string> UploadAsync(Stream data, string fileName, string contentType, CancellationToken ct = default)
        => Task.FromResult($"test-storage/{Guid.NewGuid()}/{fileName}");
}
