using System.Net.Http.Json;
using OMyFish.ShopService.Application.Interfaces;

namespace OMyFish.ShopService.Infrastructure.Payments;

/// Load-testing stand-in for Stripe/PayPal/Adyen: calls a WireMock payment gateway (fixed
/// 100ms delay — src/services/ShopService/loadtest/wiremock) so k6 exercises the real checkout
/// hot path (reserve → processor call) without real credentials or processor rate limits.
/// Only registered when Payment:Mock:Enabled=true (docker-compose.loadtest.yml) — never a
/// candidate processor in a normal run. Port of omyfish-java's MockPaymentCheckoutAdapter.
public sealed class MockPaymentCheckoutGateway(HttpClient http) : IPaymentGateway
{
    public string Name => "mock";
    public bool IsConfigured => true;

    public async Task<CheckoutSession?> CreateCheckoutSessionAsync(
        Guid orderId, IReadOnlyList<PaymentLineItem> lines, string successUrl, string cancelUrl,
        string idempotencyKey, string? customerEmail, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/checkout/sessions")
        {
            Content = JsonContent.Create(new { orderId }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        (await http.SendAsync(request, ct)).EnsureSuccessStatusCode();
        return new CheckoutSession($"mock_{idempotencyKey}", successUrl);
    }

    public async Task<RefundResult?> RefundAsync(
        string? paymentIntentId, long? amountCents, string idempotencyKey, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/refunds");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        (await http.SendAsync(request, ct)).EnsureSuccessStatusCode();
        return new RefundResult($"mock_refund_{idempotencyKey}", "succeeded", amountCents);
    }

    // The load test covers the checkout hot path, not the asynchronous payment webhook.
    public Task<PaymentEvent?> VerifyWebhookAsync(
        string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default) =>
        Task.FromResult<PaymentEvent?>(null);
}
