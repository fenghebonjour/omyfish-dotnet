using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OMyFish.IdentityService.Infrastructure.Payments;
using Xunit;

namespace OMyFish.IdentityService.Tests;

public class StripePaymentGatewayTest
{
    private const string WebhookSecret = "whsec_test_secret";

    private readonly StripePaymentGateway _gateway = new(
        secretKey: "sk_test",
        webhookSecret: WebhookSecret,
        prices: new Dictionary<string, string> { ["monthly"] = "price_monthly", ["yearly"] = "price_yearly" },
        logger: Substitute.For<ILogger<StripePaymentGateway>>());

    // BACKLOG item L (ported from omyfish-java): Stripe.net's default throws when the event's
    // api_version doesn't match the SDK's compiled version — common in a sandbox whose account
    // api_version trails the library — and VerifyWebhookAsync's catch block was swallowing that
    // as a silent drop. It must still recover the subscription via throwOnApiVersionMismatch:
    // false instead of silently dropping the event.
    [Fact]
    public async Task VerifyWebhookAsync_SubscriptionUpdated_WithMismatchedApiVersion_StillMapsEvent()
    {
        var payload = """
            {
              "id": "evt_1",
              "object": "event",
              "api_version": "2019-02-19",
              "type": "customer.subscription.updated",
              "data": {
                "object": {
                  "id": "sub_456",
                  "object": "subscription",
                  "customer": "cus_123",
                  "status": "active",
                  "cancel_at_period_end": true
                }
              }
            }
            """;

        var result = await _gateway.VerifyWebhookAsync(payload, SignedHeaders(payload));

        Assert.NotNull(result);
        Assert.Equal("evt_1", result!.EventId);
        Assert.Equal("subscription_updated", result.Type);
        Assert.Equal("cus_123", result.CustomerId);
        Assert.Equal("sub_456", result.SubscriptionId);
        Assert.Equal("active", result.ProviderStatus);
    }

    private static Dictionary<string, string> SignedHeaders(string payload)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(WebhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var signature = Convert.ToHexString(hash).ToLowerInvariant();
        return new Dictionary<string, string> { ["stripe-signature"] = $"t={timestamp},v1={signature}" };
    }
}
