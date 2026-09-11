using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OMyFish.SpeciesService.Tests;

// Endpoint-level slice tests against the real ASP.NET Core pipeline (WebApplicationFactory),
// not just the MediatR handler — real Postgres + real RabbitMQ via Testcontainers, with only
// the ai-service HTTP call and MinIO upload faked (SpeciesApiFixture). This specifically
// closes the gap the WebApplicationFactory-slice-tests follow-up flagged: /identify publishes
// through MassTransit's EF Core transactional outbox (BACKLOG.md item F §2.3), so a test that
// only hits the repository layer can't prove the outbox actually drains to a real broker.
[Collection("SpeciesApi")]
public class IdentificationEndpointTests(SpeciesApiFixture fixture)
{
    private static async Task WaitForOutboxToDrainAsync(SpeciesApiFixture fixture, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var db = fixture.CreateDbContext();
            if (await db.Set<OutboxMessage>().CountAsync() == 0) return;
            await Task.Delay(250);
        }
        throw new TimeoutException("Outbox did not drain to the real broker within the timeout.");
    }

    [Fact]
    public async Task Identify_WithImage_PersistsPredictionAndDeliversThroughTheRealOutboxAndBroker()
    {
        var scientificName = $"Esox.lucius.{Guid.NewGuid():N}";
        fixture.FakeAi.NextPredictResult = new(
            [new(scientificName, "Northern Pike", 0.92, 1, "LC", "Lakes", "Carnivore", 150, "desc", "fact")]);

        var client = fixture.CreateClient();
        using var form = new MultipartFormDataContent();
        using var imageContent = new ByteArrayContent([1, 2, 3, 4]);
        imageContent.Headers.ContentType = new("image/jpeg");
        form.Add(imageContent, "image", "pike.jpg");

        var response = await client.PostAsync("/api/v1/species/identify?topK=1", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var predictions = body.GetProperty("predictions");
        Assert.Equal(1, predictions.GetArrayLength());
        Assert.Equal(scientificName, predictions[0].GetProperty("scientificName").GetString());

        // Proves the real MassTransit bus-outbox delivery service actually published
        // FishIdentifiedEvent to the real RabbitMQ broker — not just that the DB write
        // succeeded.
        await WaitForOutboxToDrainAsync(fixture, TimeSpan.FromSeconds(15));

        await using var db = fixture.CreateDbContext();
        var species = await db.Species.SingleAsync(s => s.ScientificName == scientificName);
        Assert.Equal("Northern Pike", species.CommonName);
        var prediction = await db.Predictions.SingleAsync(p => p.ScientificName == scientificName);
        Assert.Equal(1, prediction.Rank);
    }

    [Fact]
    public async Task Identify_NoImage_ReturnsBadRequest()
    {
        var client = fixture.CreateClient();
        using var form = new MultipartFormDataContent();
        using var imageContent = new ByteArrayContent([]);
        imageContent.Headers.ContentType = new("image/jpeg");
        form.Add(imageContent, "image", "empty.jpg");

        var response = await client.PostAsync("/api/v1/species/identify", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
