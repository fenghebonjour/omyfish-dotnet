using OMyFish.ShopService.Domain.ValueObjects;

namespace OMyFish.ShopService.Application.Interfaces;

// One-time-payment gateway for Omyshop checkout — deliberately separate from IdentityService's
// subscription-shaped IPaymentGateway (fixed monthly/yearly prices); this one needs arbitrary,
// per-cart line items and a one-shot refund rather than recurring billing.
public interface IPaymentGateway
{
    bool IsConfigured { get; }

    // customerEmail pre-fills/locks Stripe's own email field — pass the guest's email for a
    // guest checkout so they aren't asked to type it twice; null for an account checkout.
    Task<CheckoutSession?> CreateCheckoutSessionAsync(
        Guid orderId, IReadOnlyList<PaymentLineItem> lines, string successUrl, string cancelUrl,
        string idempotencyKey, string? customerEmail, CancellationToken ct = default);

    Task<RefundResult?> RefundAsync(
        string? paymentIntentId, long? amountCents, string idempotencyKey, CancellationToken ct = default);

    // Returns null when the payload doesn't verify or isn't an event this gateway cares about.
    Task<PaymentEvent?> VerifyWebhookAsync(
        string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default);
}

public sealed record PaymentLineItem(string Name, long UnitPriceCents, int Quantity);

public sealed record CheckoutSession(string SessionId, string CheckoutUrl);

public sealed record RefundResult(string RefundId, string? Status, long? AmountCents);

public enum PaymentEventType { SessionCompleted, SessionExpired }

// ShippingAddress is populated for SessionCompleted (Checkout always collects it) and null for
// SessionExpired.
public sealed record PaymentEvent(
    PaymentEventType Type, string StripeSessionId, string? StripePaymentIntentId,
    ShippingAddress? ShippingAddress);
