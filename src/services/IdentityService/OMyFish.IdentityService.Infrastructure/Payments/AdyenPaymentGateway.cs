using System.Text.Json;
using Adyen.Checkout.Models;
using Adyen.Checkout.Services;
using Adyen.Core.Client;
using Adyen.Webhooks.Handlers;
using OMyFish.IdentityService.Application.Interfaces;

namespace OMyFish.IdentityService.Infrastructure.Payments;

/// Adyen Checkout Sessions — a sibling acquirer to StripePaymentGateway, not a Stripe payment
/// method. Uses the official Adyen NuGet package (36.1.0) for typed requests and HMAC webhook
/// validation (IWebhooksHandler.IsValidHmacSignature), rather than hand-rolled REST — the typed
/// services (IPaymentsService/IModificationsService/IWebhooksHandler) are DI-registered in
/// Program.cs via the SDK's own ConfigureCheckout/ConfigureWebhooks IHostBuilder extensions,
/// which is how this SDK version expects to be wired (a real shape change from the Java
/// sibling's version, which just newed up a Client directly).
///
/// Model differences from Stripe, by necessity rather than by choice (same as the Java sibling):
///  - Adyen has no subscription object at all. The first payment stores a payment method
///    (RecurringProcessingModel=Subscription, StorePaymentMethodMode=Enabled); actually renewing
///    on a monthly/yearly schedule requires charging that stored payment method on a schedule —
///    a separate follow-up feature, not built here (see BACKLOG.md). SubscriptionId is therefore
///    our own generated reference, not Adyen's; CustomerId is the userId, not a real Adyen
///    customer id.
///  - ClientSecret holds a small JSON blob {"sessionId":...,"sessionData":...} — Adyen's web
///    Components need both values, not one string.
///  - CreateSetupIntentAsync uses a zero-amount session as a known rough edge: a true
///    zero-amount verification isn't uniformly supported across every card network/region.
///  - SetDefaultPaymentMethodAsync is a documented no-op (same reasoning as the PayPal sibling)
///    — and Adyen's webhook mapping here never emits payment_method_attached anyway, so it's
///    genuinely unreachable, not a hidden gap.
///  - RefundSubscriptionAsync requires lastPaymentReference (Adyen's pspReference of the last
///    captured payment) since Adyen has no subscription object to look it up from.
public class AdyenPaymentGateway : IPaymentGateway
{
    private readonly IPaymentsService _payments;
    private readonly IModificationsService _modifications;
    private readonly IWebhooksHandler _webhooks;
    private readonly string _apiKey;
    private readonly string _merchantAccount;
    private readonly string _hmacKey;
    private readonly string _currency;
    private readonly long _priceMonthlyCents;
    private readonly long _priceYearlyCents;
    private readonly string _returnUrl;

    public AdyenPaymentGateway(
        IPaymentsService payments, IModificationsService modifications, IWebhooksHandler webhooks,
        string apiKey, string merchantAccount, string hmacKey, string currency,
        long priceMonthlyCents, long priceYearlyCents, string returnUrl)
    {
        _payments = payments;
        _modifications = modifications;
        _webhooks = webhooks;
        _apiKey = apiKey;
        _merchantAccount = merchantAccount;
        _hmacKey = hmacKey;
        _currency = currency;
        _priceMonthlyCents = priceMonthlyCents;
        _priceYearlyCents = priceYearlyCents;
        _returnUrl = returnUrl;
    }

    public string Name => "adyen";

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_merchantAccount);

    public async Task<SubscriptionIntentResult?> CreateSubscriptionIntentAsync(
        Guid userId, string email, string plan, string idempotencyKey, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrEmpty(_returnUrl)) return null;

        var priceCents = plan == "yearly" ? _priceYearlyCents : _priceMonthlyCents;
        var reference = $"{userId}:{plan}:{Guid.NewGuid()}";
        var request = new CreateCheckoutSessionRequest
        {
            MerchantAccount = _merchantAccount,
            Reference = reference,
            Amount = new Amount { Currency = _currency, Value = priceCents },
            ReturnUrl = _returnUrl,
            ShopperReference = userId.ToString(),
            ShopperEmail = email,
            RecurringProcessingModel = CreateCheckoutSessionRequest.RecurringProcessingModelEnum.Subscription,
            StorePaymentMethodMode = CreateCheckoutSessionRequest.StorePaymentMethodModeEnum.Enabled,
        };

        try
        {
            var response = await _payments.SessionsAsync(
                request, new RequestOptions().AddIdempotencyKey(idempotencyKey), ct);
            if (!((ICreated<CreateCheckoutSessionResponse>)response).TryDeserializeCreatedResponse(out var created))
                return null;

            var clientSecret = JsonSerializer.Serialize(
                new { sessionId = created.Id, sessionData = created.SessionData });
            return new SubscriptionIntentResult(Name, userId.ToString(), reference, clientSecret, "pending");
        }
        catch (ApiException e)
        {
            throw new InvalidOperationException($"Adyen checkout session failed: {e.Message}", e);
        }
    }

    public async Task<SetupIntentResult?> CreateSetupIntentAsync(
        Guid userId, string email, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrEmpty(_returnUrl)) return null;

        var reference = $"{userId}:setup:{Guid.NewGuid()}";
        var request = new CreateCheckoutSessionRequest
        {
            MerchantAccount = _merchantAccount,
            Reference = reference,
            // Known rough edge: a true zero-amount verification isn't uniformly supported
            // across every card network/region — validate against the schemes actually in
            // play before relying on this in production.
            Amount = new Amount { Currency = _currency, Value = 0L },
            ReturnUrl = _returnUrl,
            ShopperReference = userId.ToString(),
            ShopperEmail = email,
            RecurringProcessingModel = CreateCheckoutSessionRequest.RecurringProcessingModelEnum.Subscription,
            StorePaymentMethodMode = CreateCheckoutSessionRequest.StorePaymentMethodModeEnum.Enabled,
        };

        try
        {
            var response = await _payments.SessionsAsync(request, cancellationToken: ct);
            if (!((ICreated<CreateCheckoutSessionResponse>)response).TryDeserializeCreatedResponse(out var created))
                return null;

            var clientSecret = JsonSerializer.Serialize(
                new { sessionId = created.Id, sessionData = created.SessionData });
            return new SetupIntentResult(Name, userId.ToString(), clientSecret);
        }
        catch (ApiException e)
        {
            throw new InvalidOperationException($"Adyen setup session failed: {e.Message}", e);
        }
    }

    public Task SetDefaultPaymentMethodAsync(string customerId, string paymentMethodId, CancellationToken ct = default)
        // No-op: Adyen has no customer-level "default payment method" pointer, and this
        // gateway's webhook mapping never emits payment_method_attached for Adyen anyway.
        => Task.CompletedTask;

    public async Task<RefundResult?> RefundSubscriptionAsync(
        string subscriptionId, long? amountCents, string idempotencyKey,
        string? lastPaymentReference = null, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrEmpty(lastPaymentReference)) return null;

        var request = new PaymentRefundRequest
        {
            MerchantAccount = _merchantAccount,
            Reference = idempotencyKey,
        };
        if (amountCents is not null)
            request.Amount = new Amount { Currency = _currency, Value = amountCents.Value };

        try
        {
            var response = await _modifications.RefundCapturedPaymentAsync(
                lastPaymentReference, request, new RequestOptions().AddIdempotencyKey(idempotencyKey), ct);
            if (!((ICreated<PaymentRefundResponse>)response).TryDeserializeCreatedResponse(out var created))
                return null;

            return new RefundResult(created.PspReference, created.Status.ToString(), amountCents);
        }
        catch (ApiException e)
        {
            throw new InvalidOperationException($"Adyen refund failed: {e.Message}", e);
        }
    }

    public Task<PaymentEvent?> VerifyWebhookAsync(
        string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrEmpty(_hmacKey))
            return Task.FromResult<PaymentEvent?>(null);

        try
        {
            var notification = _webhooks.DeserializeNotificationRequest(payload);
            var item = notification.NotificationItemContainers?.FirstOrDefault()?.NotificationItem;
            if (item is null) return Task.FromResult<PaymentEvent?>(null);
            if (!_webhooks.IsValidHmacSignature(item)) return Task.FromResult<PaymentEvent?>(null);

            // Adyen has no subscription-lifecycle events to speak of since it never hosts the
            // subscription itself — only a successful capture is meaningful to us here.
            if (item.EventCode != "AUTHORISATION" || !item.Success)
                return Task.FromResult<PaymentEvent?>(null);

            // merchantReference is our own "userId:plan:guid" reference (see
            // CreateSubscriptionIntentAsync) — there's no shopperReference on a notification
            // item, so the customer id is recovered from the reference we control instead.
            var reference = item.MerchantReference;
            var customerId = reference is not null && reference.Contains(':')
                ? reference[..reference.IndexOf(':')] : reference;

            return Task.FromResult<PaymentEvent?>(new PaymentEvent(
                "payment_captured", null, customerId, null, null, null, null,
                EventId: item.PspReference, Processor: Name, PaymentReference: item.PspReference));
        }
        catch
        {
            return Task.FromResult<PaymentEvent?>(null);
        }
    }

    public Task<IReadOnlyList<ReconciliationCandidate>> ListRecentSubscriptionsAsync(
        DateTime since, CancellationToken ct = default)
        // Not implemented: Adyen isn't live yet (same known gap as the Java sibling's
        // BACKLOG.md item I.6) — it would also need Adyen's own notion of "recent" (no
        // subscription object to list; this would have to query captured payments by
        // shopperReference/merchantReference instead).
        => Task.FromResult<IReadOnlyList<ReconciliationCandidate>>([]);

    public Task<string?> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken ct = default)
        // Adyen has no hosted self-service portal equivalent to Stripe's (BACKLOG.md item J.1)
        // — cancel/upgrade for Adyen subscribers is admin-assisted for now.
        => Task.FromResult<string?>(null);
}
