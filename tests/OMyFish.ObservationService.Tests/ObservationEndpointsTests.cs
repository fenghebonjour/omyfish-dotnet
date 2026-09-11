using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MassTransit.EntityFrameworkCoreIntegration;
using Xunit;

namespace OMyFish.ObservationService.Tests;

// Endpoint-level slice tests against the real ASP.NET Core pipeline (WebApplicationFactory),
// not just MediatR handlers — real Postgres + real RabbitMQ via Testcontainers, nothing mocked.
// This specifically closes the gap the WebApplicationFactory-slice-tests follow-up flagged:
// the create endpoint publishes through MassTransit's EF Core transactional outbox (BACKLOG.md
// item F §2.3), so a test that only hits the repository layer can't prove the outbox actually
// drains to a real broker — only an end-to-end HTTP request through the full host can.
[Collection("ObservationApi")]
public class ObservationEndpointsTests(ObservationApiFixture fixture)
{
    private const string DevJwtSecret = "dev-secret-change-in-production-min-32-chars";

    private static string BuildAccessToken(Guid userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DevJwtSecret));
        var token = new JwtSecurityToken(
            claims: [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, "observation-test@example.com"),
                new Claim(ClaimTypes.Role, "USER"),
                new Claim("token_type", "access"),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task WaitForOutboxToDrainAsync(ObservationApiFixture fixture, TimeSpan timeout)
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
    public async Task CreateObservation_WithoutToken_Returns401()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/observations", new
        {
            speciesName = "Walleye",
            scientificName = "Sander vitreus",
            topConfidence = 0.9,
            imageStorageKey = "stored/test.jpg",
            latitude = 45.5,
            longitude = -73.5,
            notes = (string?)null,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateObservation_WithValidToken_PersistsAndDeliversThroughTheRealOutboxAndBroker()
    {
        var client = fixture.CreateClient();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BuildAccessToken(userId));

        var response = await client.PostAsJsonAsync("/api/v1/observations", new
        {
            speciesName = "Walleye",
            scientificName = "Sander vitreus",
            topConfidence = 0.9,
            imageStorageKey = "stored/test.jpg",
            latitude = 45.5,
            longitude = -73.5,
            notes = "Caught near the dock",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var observationId = body.GetProperty("observationId").GetGuid();

        // Proves the real MassTransit bus-outbox delivery service actually published this
        // row to the real RabbitMQ broker — not just that the DB write succeeded.
        await WaitForOutboxToDrainAsync(fixture, TimeSpan.FromSeconds(15));

        var getResponse = await client.GetAsync($"/api/v1/observations/{observationId}");
        getResponse.EnsureSuccessStatusCode();
        var saved = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Walleye", saved.GetProperty("speciesName").GetString());
        Assert.Equal(userId, saved.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task GetObservationsGeoJson_IsPublic_Returns200()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/v1/observations/geojson");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetNearbyObservations_InvalidRadius_Returns400BadRequest()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/v1/observations/nearby?lat=45.5&lon=-73.5&radiusKm=-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
