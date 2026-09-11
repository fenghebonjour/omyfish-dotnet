using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace OMyFish.ApiGateway.Tests;

// Regression coverage for BACKLOG.md item F, WEAKNESS_AUDIT.md §1.1: the gateway used to
// configure JWT auth but never call .RequireAuthorization() on the proxy, so every route was
// reachable with no token at all regardless of appsettings.json's "AuthorizationPolicy". These
// tests exercise the real HTTP pipeline (not just config parsing) since that's exactly the kind
// of wiring bug config-only tests can't catch.
public class GatewayAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DevJwtSecret = "dev-secret-change-in-production-min-32-chars";
    private readonly WebApplicationFactory<Program> _factory;

    public GatewayAuthorizationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string BuildAccessToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DevJwtSecret));
        var token = new JwtSecurityToken(
            claims: [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Email, "gateway-test@example.com"),
                new Claim(ClaimTypes.Role, "USER"),
                new Claim("token_type", "access"),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Theory]
    [InlineData("/api/v1/observations/mine")]
    [InlineData("/api/v1/notifications/")]
    [InlineData("/api/v1/billing/whatever")]
    [InlineData("/api/v1/admin/whatever")]
    public async Task ProtectedRoute_WithoutToken_Returns401(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/observations/mine")]
    [InlineData("/api/v1/notifications/")]
    public async Task ProtectedRoute_WithValidToken_IsNotRejectedByAuth(string path)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BuildAccessToken());

        var response = await client.GetAsync(path);

        // The downstream service isn't running in this test host, so the proxy call itself
        // fails (502/503) — the point of this test is only that authorization let it through.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/species/identify")]
    public async Task AnonymousPolicyRoute_WithoutToken_IsNotRejectedByAuth(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
