using System.Text.Json;
using Adyen.Checkout.Models;
using Adyen.Checkout.Services;
using Adyen.Core.Client;
using Adyen.Webhooks.Handlers;
using OMyFish.ShopService.Application.Interfaces;

namespace OMyFish.ShopService.Infrastructure.Payments;

/// Adyen Checkout Sessions for one-shot cart checkout — a sibling gateway to
/// StripeCheckoutGateway behind the same (shop) IPaymentGateway, not IdentityService's
/// subscription-shaped AdyenPaymentGateway. Same SDK wiring (DI-registered typed services via
/// ConfigureCheckout/ConfigureWebhooks in Program.cs), minus the recurring-payment fields.
/// Port of omyfish-java's AdyenCheckoutAdapter.
///
/// Model differences from Stripe, by necessity:
///  - CheckoutUrl holds a JSON blob {"sessionId":...,"sessionData":...}, not a redirect URL —
///    Adyen's Sessions response powers the frontend's embedded Drop-in, not a hosted redirect.
///  - No shipping-address collection: Adyen's Drop-in has no built-in address step, so an
///    Adyen-paid order's ShippingAddress stays null after webhook completion (same documented
///    gap as the Java sibling).
///  - The AUTHORISATION webhook only echoes back our merchantReference (the order id), not the
///    session id — so the event carries OrderId explicitly. (omyfish-java's adapter puts the
///    order id in the session-id slot instead, which never matches the stored session id.)
///  - Same abandoned-checkout gap as PayPal — covered by StaleOrderSweepService.
public class AdyenCheckoutGateway : IPaymentGateway
{
    private readonly IPaymentsService _payments;
    private readonly IModificationsService _modifications;
    private readonly IWebhooksHandler _webhooks;
    private readonly string _apiKey;
    private readonly string _merchantAccount;
    private readonly string _hmacKey;
    private readonly string _currency;

    public AdyenCheckoutGateway(
        IPaymentsService payments, IModificationsService modifications, IWebhooksHandler webhooks,
        string apiKey, string merchantAccount, string hmacKey, string currency)
    {
        _payments = payments;
        _modifications = modifications;
        _webhooks = webhooks;
        _apiKey = apiKey;
        _merchantAccount = merchantAccount;
        _hmacKey = hmacKey;
        _currency = currency.ToUpperInvariant();
    }

    public string Name => "adyen";

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_merchantAccount);

    public async Task<CheckoutSession?> CreateCheckoutSessionAsync(
        Guid orderId, IReadOnlyList<PaymentLineItem> lines, string successUrl, string cancelUrl,
        string idempotencyKey, string? customerEmail, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        var request = new CreateCheckoutSessionRequest
        {
            MerchantAccount = _merchantAccount,
            Reference = orderId.ToString(),
            Amount = new Amount { Currency = _currency, Value = lines.Sum(l => l.UnitPriceCents * l.Quantity) },
            ReturnUrl = successUrl,
        };
        if (!string.IsNullOrWhiteSpace(customerEmail)) request.ShopperEmail = customerEmail;

        try
        {
            var response = await _payments.SessionsAsync(
                request, new RequestOptions().AddIdempotencyKey(idempotencyKey), ct);
            if (!((ICreated<CreateCheckoutSessionResponse>)response).TryDeserializeCreatedResponse(out var created))
                return null;

            var checkoutUrl = JsonSerializer.Serialize(new { sessionId = created.Id, sessionData = created.SessionData });
            return new CheckoutSession(created.Id, checkoutUrl);
        }
        catch (ApiException e)
        {
            throw new InvalidOperationException($"Adyen checkout session failed: {e.Message}", e);
        }
    }

    public async Task<RefundResult?> RefundAsync(
        string? paymentIntentId, long? amountCents, string idempotencyKey, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrEmpty(paymentIntentId)) return null;

        var request = new PaymentRefundRequest { MerchantAccount = _merchantAccount, Reference = idempotencyKey };
        if (amountCents is not null)
            request.Amount = new Amount { Currency = _currency, Value = amountCents.Value };

        try
        {
            var response = await _modifications.RefundCapturedPaymentAsync(
                paymentIntentId, request, new RequestOptions().AddIdempotencyKey(idempotencyKey), ct);
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
        if (!IsConfigured || string.IsNullOrEmpty(_hmacKey)) return Task.FromResult<PaymentEvent?>(null);

        try
        {
            var notification = _webhooks.DeserializeNotificationRequest(payload);
            var item = notification.NotificationItemContainers?.FirstOrDefault()?.NotificationItem;
            if (item is null || !_webhooks.IsValidHmacSignature(item)) return Task.FromResult<PaymentEvent?>(null);
            if (item.EventCode != "AUTHORISATION" || !item.Success) return Task.FromResult<PaymentEvent?>(null);
            if (!Guid.TryParse(item.MerchantReference, out var orderId)) return Task.FromResult<PaymentEvent?>(null);

            return Task.FromResult<PaymentEvent?>(new PaymentEvent(
                PaymentEventType.SessionCompleted, StripeSessionId: "", item.PspReference,
                ShippingAddress: null, OrderId: orderId));
        }
        catch
        {
            return Task.FromResult<PaymentEvent?>(null);
        }
    }
}
