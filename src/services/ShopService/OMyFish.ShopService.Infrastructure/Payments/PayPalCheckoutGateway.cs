using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.ValueObjects;

namespace OMyFish.ShopService.Infrastructure.Payments;

/// Direct PayPal integration (Orders v2 API) for one-shot cart checkout — a sibling gateway to
/// StripeCheckoutGateway behind the same (shop) IPaymentGateway, not IdentityService's
/// subscription-shaped PayPalPaymentGateway (different API family: Orders, not Subscriptions).
/// Same OAuth-token caching and webhook-signature verification as that gateway; port of
/// omyfish-java's PayPalCheckoutAdapter.
///
/// Model differences from Stripe, by necessity:
///  - CheckoutUrl is PayPal's "approve" link, same idea as Stripe's hosted Checkout URL.
///  - intent=CAPTURE never auto-captures — PayPal requires an explicit capture call after the
///    buyer approves. Checkout is entirely webhook-driven (no backend "return" endpoint the
///    browser hits), so capture happens *inside* VerifyWebhookAsync on CHECKOUT.ORDER.APPROVED;
///    the capture response becomes the SessionCompleted event. The later
///    PAYMENT.CAPTURE.COMPLETED webhook is redundant and ignored.
///  - A redelivered CHECKOUT.ORDER.APPROVED after a capture already succeeded (e.g. the process
///    crashed before the order got marked Paid locally) hits PayPal's ORDER_ALREADY_CAPTURED
///    error — handled by GET-ing the order's existing capture instead of failing the webhook.
///  - No "abandoned checkout" webhook the way Stripe's checkout.session.expired is — PayPal
///    Orders just stop being approvable after 3 hours. Restocking relies on
///    StaleOrderSweepService.
public class PayPalCheckoutGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions IgnoreNulls =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _webhookId;
    private readonly string _currency;

    private readonly object _tokenLock = new();
    private string? _cachedToken;
    private DateTime _tokenExpiry;

    public PayPalCheckoutGateway(HttpClient http, string clientId, string clientSecret, string webhookId, string currency)
    {
        _http = http;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _webhookId = webhookId;
        _currency = currency.ToUpperInvariant();
    }

    public string Name => "paypal";

    public bool IsConfigured => !string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_clientSecret);

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        lock (_tokenLock)
        {
            if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiry)
                return _cachedToken;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}")));
        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
            ?? throw new InvalidOperationException("PayPal token request returned no body");

        lock (_tokenLock)
        {
            _cachedToken = body.access_token;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(Math.Max(0, body.expires_in - 60));
            return _cachedToken;
        }
    }

    // idempotencyKey goes out as PayPal's own PayPal-Request-Id, so a retried call returns
    // PayPal's original result instead of creating a duplicate order/refund.
    private async Task<HttpRequestMessage> AuthorizedAsync(
        HttpMethod method, string uri, object? body, string? requestId, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, uri);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: IgnoreNulls);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
        if (requestId is not null) request.Headers.Add("PayPal-Request-Id", requestId);
        return request;
    }

    public async Task<CheckoutSession?> CreateCheckoutSessionAsync(
        Guid orderId, IReadOnlyList<PaymentLineItem> lines, string successUrl, string cancelUrl,
        string idempotencyKey, string? customerEmail, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;
        try
        {
            var totalCents = lines.Sum(l => l.UnitPriceCents * l.Quantity);
            var unit = new PurchaseUnit(
                orderId.ToString(),
                new PurchaseUnitAmount(_currency, CentsToAmount(totalCents),
                    new AmountBreakdown(new Amount(_currency, CentsToAmount(totalCents)))),
                lines.Select(l => new OrderItem(
                    l.Name, l.Quantity.ToString(), new Amount(_currency, CentsToAmount(l.UnitPriceCents)))).ToList(),
                string.IsNullOrWhiteSpace(customerEmail) ? null : new Payer(customerEmail));
            var body = new OrderCreateRequest("CAPTURE", [unit], new ApplicationContext(successUrl, cancelUrl));

            using var request = await AuthorizedAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, ct);
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var created = await response.Content.ReadFromJsonAsync<OrderCreateResponse>(ct);
            var approveUrl = created?.links?.FirstOrDefault(l => l.rel == "approve")?.href;
            return created is null || approveUrl is null ? null : new CheckoutSession(created.id, approveUrl);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"PayPal checkout failed: {e.Message}", e);
        }
    }

    public async Task<RefundResult?> RefundAsync(
        string? paymentIntentId, long? amountCents, string idempotencyKey, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrEmpty(paymentIntentId)) return null;
        try
        {
            var body = new RefundCreateRequest(
                amountCents is null ? null : new Amount(_currency, CentsToAmount(amountCents.Value)));
            using var request = await AuthorizedAsync(
                HttpMethod.Post, $"/v2/payments/captures/{Uri.EscapeDataString(paymentIntentId)}/refund",
                body, idempotencyKey, ct);
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var refund = await response.Content.ReadFromJsonAsync<RefundCreateResponse>(ct);
            return refund is null ? null : new RefundResult(refund.id, refund.status, amountCents);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"PayPal refund failed: {e.Message}", e);
        }
    }

    public async Task<PaymentEvent?> VerifyWebhookAsync(
        string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrEmpty(_webhookId)) return null;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch
        {
            return null;
        }
        if (root is null) return null;

        try
        {
            using var request = await AuthorizedAsync(HttpMethod.Post, "/v1/notifications/verify-webhook-signature",
                new VerifyWebhookRequest(
                    headers.GetValueOrDefault("paypal-auth-algo"),
                    headers.GetValueOrDefault("paypal-cert-url"),
                    headers.GetValueOrDefault("paypal-transmission-id"),
                    headers.GetValueOrDefault("paypal-transmission-sig"),
                    headers.GetValueOrDefault("paypal-transmission-time"),
                    _webhookId,
                    root), requestId: null, ct);
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var verification = await response.Content.ReadFromJsonAsync<VerifyWebhookResponse>(ct);
            if (verification?.verification_status != "SUCCESS") return null;
        }
        catch
        {
            return null;
        }

        // PAYMENT.CAPTURE.COMPLETED (fired right after our own capture call below) and
        // everything else: nothing left to do, the APPROVED branch already drove completion.
        if (TextOrNull(root["event_type"]) != "CHECKOUT.ORDER.APPROVED") return null;

        var resource = root["resource"];
        var paypalOrderId = TextOrNull(resource?["id"]);
        if (paypalOrderId is null) return null;

        var captured = await CaptureOrRecoverCaptureAsync(paypalOrderId, ct);
        var captureId = TextOrNull(captured?["purchase_units"]?[0]?["payments"]?["captures"]?[0]?["id"]);
        if (captureId is null) return null;

        return new PaymentEvent(
            PaymentEventType.SessionCompleted, paypalOrderId, captureId, ExtractShippingAddress(resource));
    }

    private async Task<JsonNode?> CaptureOrRecoverCaptureAsync(string paypalOrderId, CancellationToken ct)
    {
        var escaped = Uri.EscapeDataString(paypalOrderId);
        using var capture = await AuthorizedAsync(
            HttpMethod.Post, $"/v2/checkout/orders/{escaped}/capture", new { }, $"capture:{paypalOrderId}", ct);
        var response = await _http.SendAsync(capture, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode) return JsonNode.Parse(body);

        if (!body.Contains("ORDER_ALREADY_CAPTURED"))
            throw new InvalidOperationException($"PayPal capture failed: {(int)response.StatusCode} {body}");

        // A redelivered CHECKOUT.ORDER.APPROVED after a prior capture already succeeded —
        // recover the existing capture instead of failing the webhook.
        using var lookup = await AuthorizedAsync(HttpMethod.Get, $"/v2/checkout/orders/{escaped}", null, null, ct);
        var existing = await _http.SendAsync(lookup, ct);
        existing.EnsureSuccessStatusCode();
        return JsonNode.Parse(await existing.Content.ReadAsStringAsync(ct));
    }

    private static ShippingAddress? ExtractShippingAddress(JsonNode? orderResource)
    {
        var shipping = orderResource?["purchase_units"]?[0]?["shipping"];
        var address = shipping?["address"];
        if (address is null) return null;
        return new ShippingAddress(
            TextOrNull(shipping?["name"]?["full_name"]),
            TextOrNull(address["address_line_1"]),
            TextOrNull(address["address_line_2"]),
            TextOrNull(address["admin_area_2"]),
            TextOrNull(address["admin_area_1"]),
            TextOrNull(address["postal_code"]),
            TextOrNull(address["country_code"]));
    }

    private static string? TextOrNull(JsonNode? node) =>
        node is null || node.GetValueKind() == JsonValueKind.Null ? null : node.ToString();

    private static string CentsToAmount(long amountCents) => $"{amountCents / 100}.{amountCents % 100:D2}";

    private sealed record TokenResponse(string access_token, string token_type, long expires_in);

    private sealed record Amount(string currency_code, string value);
    private sealed record AmountBreakdown(Amount item_total);
    private sealed record PurchaseUnitAmount(string currency_code, string value, AmountBreakdown breakdown);
    private sealed record OrderItem(string name, string quantity, Amount unit_amount);
    private sealed record Payer(string email_address);
    private sealed record PurchaseUnit(
        string reference_id, PurchaseUnitAmount amount, List<OrderItem> items, Payer? payer);
    private sealed record ApplicationContext(string return_url, string cancel_url);
    private sealed record OrderCreateRequest(
        string intent, List<PurchaseUnit> purchase_units, ApplicationContext application_context);
    private sealed record Link(string rel, string href);
    private sealed record OrderCreateResponse(string id, string status, List<Link>? links);

    private sealed record RefundCreateRequest(Amount? amount);
    private sealed record RefundCreateResponse(string id, string status);

    private sealed record VerifyWebhookRequest(
        string? auth_algo, string? cert_url, string? transmission_id,
        string? transmission_sig, string? transmission_time, string webhook_id,
        JsonNode webhook_event);
    private sealed record VerifyWebhookResponse(string verification_status);
}
