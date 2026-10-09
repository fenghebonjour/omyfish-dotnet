using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OMyFish.IdentityService.Application.Interfaces;

namespace OMyFish.IdentityService.Infrastructure.Payments;

/// Direct PayPal integration (Subscriptions + Vault APIs) via a plain HttpClient — PayPal has
/// no first-party .NET SDK worth depending on for this, same reasoning as the Java sibling's
/// Spring RestClient-based adapter. A sibling gateway to StripePaymentGateway behind the same
/// IPaymentGateway, not a Stripe payment method.
///
/// Model differences from Stripe, by necessity rather than by choice:
///  - No client-secret concept: SubscriptionIntentResult/SetupIntentResult's ClientSecret slot
///    instead carries the "approve" link the frontend must redirect the shopper to.
///  - CustomerId is unknown at subscription-creation time (PayPal only assigns a payer_id once
///    the shopper approves) — it starts empty and is filled in by the ACTIVATED webhook, the
///    same way Stripe's own webhook refines state after creation.
///  - SetDefaultPaymentMethodAsync is a no-op: PayPal's Vault API has no customer-level
///    "default payment method" pointer the way Stripe does.
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _webhookId;
    private readonly string _returnUrl;
    private readonly string _cancelUrl;
    private readonly IReadOnlyDictionary<string, string> _planIds;

    private readonly object _tokenLock = new();
    private string? _cachedToken;
    private DateTime _tokenExpiry;

    public PayPalPaymentGateway(
        HttpClient http, string clientId, string clientSecret, string webhookId,
        string returnUrl, string cancelUrl, IReadOnlyDictionary<string, string> planIds)
    {
        _http = http;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _webhookId = webhookId;
        _returnUrl = returnUrl;
        _cancelUrl = cancelUrl;
        _planIds = planIds;
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

    public async Task<SubscriptionIntentResult?> CreateSubscriptionIntentAsync(
        Guid userId, string email, string plan, string idempotencyKey, CancellationToken ct = default)
    {
        if (!_planIds.TryGetValue(plan, out var planId) || string.IsNullOrEmpty(planId))
            return null;
        if (!IsConfigured || string.IsNullOrEmpty(_returnUrl) || string.IsNullOrEmpty(_cancelUrl))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/billing/subscriptions")
        {
            Content = JsonContent.Create(new SubscriptionCreateRequest(
                planId, new Subscriber(email), new ApplicationContext(_returnUrl, _cancelUrl))),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
        request.Headers.Add("PayPal-Request-Id", idempotencyKey);

        try
        {
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<SubscriptionCreateResponse>(ct);
            if (body is null) return null;

            // CustomerId left blank: PayPal only assigns a payer_id once the shopper approves;
            // the ACTIVATED webhook fills it in.
            return new SubscriptionIntentResult(Name, "", body.id, ApproveLink(body.links), body.status);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"PayPal checkout failed: {e.Message}", e);
        }
    }

    public async Task<SetupIntentResult?> CreateSetupIntentAsync(
        Guid userId, string email, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        // Creates a Vault setup token for the PayPal wallet; the frontend's PayPal JS SDK
        // completes it (exchanges it for a reusable payment token) after the shopper approves,
        // the same way Stripe.js completes a SetupIntent client-side.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v3/vault/setup-tokens")
        {
            Content = JsonContent.Create(new SetupTokenRequest(new PaymentSource(
                new PayPalWalletSource("IMMEDIATE", "MERCHANT", "CONSUMER", false,
                    new ApplicationContext(_returnUrl, _cancelUrl))))),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));

        try
        {
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<SetupTokenResponse>(ct);
            if (body is null) return null;
            return new SetupIntentResult(Name, "", ApproveLink(body.links) ?? "");
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"PayPal setup token failed: {e.Message}", e);
        }
    }

    public Task SetDefaultPaymentMethodAsync(string customerId, string paymentMethodId, CancellationToken ct = default)
        // No-op: PayPal's Vault API has no customer-level "default payment method" the way
        // Stripe does — the payment-token id is referenced directly on each future charge.
        => Task.CompletedTask;

    public async Task<RefundResult?> RefundSubscriptionAsync(
        string subscriptionId, long? amountCents, string idempotencyKey,
        string? lastPaymentReference = null, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrEmpty(subscriptionId)) return null;

        try
        {
            var token = await AccessTokenAsync(ct);
            var now = DateTime.UtcNow;
            using var listRequest = new HttpRequestMessage(HttpMethod.Get,
                $"/v1/billing/subscriptions/{subscriptionId}/transactions" +
                $"?start_time={Uri.EscapeDataString(now.AddDays(-366).ToString("o"))}" +
                $"&end_time={Uri.EscapeDataString(now.ToString("o"))}");
            listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var listResponse = await _http.SendAsync(listRequest, ct);
            listResponse.EnsureSuccessStatusCode();
            var transactions = await listResponse.Content.ReadFromJsonAsync<TransactionsResponse>(ct);
            if (transactions?.transactions is null) return null;

            // The latest COMPLETED transaction's id doubles as the capture id for refund
            // purposes, per PayPal's Subscriptions transactions endpoint.
            var captureId = transactions.transactions.LastOrDefault(t => t.status == "COMPLETED")?.id;
            if (captureId is null) return null;

            using var refundRequest = new HttpRequestMessage(
                HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund")
            {
                Content = JsonContent.Create(amountCents is null
                    ? new RefundCreateRequest(null)
                    : new RefundCreateRequest(new Amount(CentsToAmount(amountCents.Value), "USD"))),
            };
            refundRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            refundRequest.Headers.Add("PayPal-Request-Id", idempotencyKey);
            var refundResponse = await _http.SendAsync(refundRequest, ct);
            refundResponse.EnsureSuccessStatusCode();
            var refundBody = await refundResponse.Content.ReadFromJsonAsync<RefundCreateResponse>(ct);
            if (refundBody is null) return null;

            return new RefundResult(refundBody.id, refundBody.status, amountCents);
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
            var token = await AccessTokenAsync(ct);
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/v1/notifications/verify-webhook-signature")
            {
                Content = JsonContent.Create(new VerifyWebhookRequest(
                    headers.GetValueOrDefault("paypal-auth-algo"),
                    headers.GetValueOrDefault("paypal-cert-url"),
                    headers.GetValueOrDefault("paypal-transmission-id"),
                    headers.GetValueOrDefault("paypal-transmission-sig"),
                    headers.GetValueOrDefault("paypal-transmission-time"),
                    _webhookId,
                    root)),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var verification = await response.Content.ReadFromJsonAsync<VerifyWebhookResponse>(ct);
            if (verification?.verification_status != "SUCCESS") return null;
        }
        catch
        {
            return null;
        }

        var eventId = TextOrNull(root["id"]);
        var eventType = TextOrNull(root["event_type"]);
        var resource = root["resource"];

        return (eventType ?? "") switch
        {
            "BILLING.SUBSCRIPTION.ACTIVATED" or "BILLING.SUBSCRIPTION.UPDATED" => new PaymentEvent(
                "subscription_updated", null,
                TextOrNull(resource?["subscriber"]?["payer_id"]),
                TextOrNull(resource?["id"]),
                PlanForPlanId(TextOrNull(resource?["plan_id"])),
                TextOrNull(resource?["status"]),
                ParseDate(TextOrNull(resource?["billing_info"]?["next_billing_time"])),
                EventId: eventId, Processor: Name),
            "BILLING.SUBSCRIPTION.CANCELLED" or "BILLING.SUBSCRIPTION.EXPIRED" or "BILLING.SUBSCRIPTION.SUSPENDED" =>
                new PaymentEvent(
                    "subscription_deleted", null,
                    TextOrNull(resource?["subscriber"]?["payer_id"]),
                    TextOrNull(resource?["id"]),
                    null, null, null, EventId: eventId, Processor: Name),
            "VAULT.PAYMENT-TOKEN.CREATED" => new PaymentEvent(
                "payment_method_attached", null,
                TextOrNull(resource?["customer"]?["id"]),
                null, null, null, null,
                TextOrNull(resource?["id"]), eventId, Name),
            _ => null,
        };
    }

    public Task<IReadOnlyList<ReconciliationCandidate>> ListRecentSubscriptionsAsync(
        DateTime since, CancellationToken ct = default)
        // Not implemented: PayPal's own reconciliation support is a known, separately-tracked
        // gap (BACKLOG.md item I.6 in the Java sibling) — add this once it's actually needed.
        => Task.FromResult<IReadOnlyList<ReconciliationCandidate>>([]);

    public Task<string?> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken ct = default)
        // PayPal has no hosted self-service portal equivalent to Stripe's (BACKLOG.md item J.1)
        // — cancel/upgrade for PayPal subscribers is admin-assisted for now.
        => Task.FromResult<string?>(null);

    private static string? ApproveLink(List<Link>? links) =>
        links?.FirstOrDefault(l => l.rel == "approve")?.href;

    /// Reverses _planIds to recover our plan name from a PayPal plan id on a webhook event.
    private string? PlanForPlanId(string? planId)
    {
        if (planId is null) return null;
        return _planIds.FirstOrDefault(kv => kv.Value == planId).Key;
    }

    private static string? TextOrNull(JsonNode? node) =>
        node is null || node.GetValueKind() == JsonValueKind.Null ? null : node.ToString();

    private static DateTime? ParseDate(string? iso) =>
        iso is not null && DateTime.TryParse(iso, out var dt) ? dt : null;

    private static string CentsToAmount(long amountCents) =>
        $"{amountCents / 100}.{amountCents % 100:D2}";

    private sealed record TokenResponse(string access_token, string token_type, long expires_in);

    private sealed record Subscriber(string email_address);
    private sealed record ApplicationContext(string return_url, string cancel_url);
    private sealed record SubscriptionCreateRequest(
        string plan_id, Subscriber subscriber, ApplicationContext application_context);
    private sealed record Link(string rel, string href);
    private sealed record SubscriptionCreateResponse(string id, string status, List<Link>? links);

    private sealed record PayPalWalletSource(
        string usage_pattern, string usage_type, string customer_type,
        bool permit_multiple_payment_tokens, ApplicationContext experience_context);
    private sealed record PaymentSource(PayPalWalletSource paypal);
    private sealed record SetupTokenRequest(PaymentSource payment_source);
    private sealed record SetupTokenResponse(string id, string status, List<Link>? links);

    private sealed record Transaction(string id, string status);
    private sealed record TransactionsResponse(List<Transaction>? transactions);
    private sealed record Amount(string value, string currency_code);
    private sealed record RefundCreateRequest(Amount? amount);
    private sealed record RefundCreateResponse(string id, string status);

    private sealed record VerifyWebhookRequest(
        string? auth_algo, string? cert_url, string? transmission_id,
        string? transmission_sig, string? transmission_time, string webhook_id,
        JsonNode webhook_event);
    private sealed record VerifyWebhookResponse(string verification_status);
}
