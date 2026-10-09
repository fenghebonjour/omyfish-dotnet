namespace OMyFish.IdentityService.Application.Interfaces;

public interface IPaymentGateway
{
    /// Stable id for this gateway, e.g. "stripe" — used to route refunds/webhooks/dedup.
    string Name { get; }

    bool IsConfigured { get; }

    /// Null when not configured or the plan has no price configured. idempotencyKey is also
    /// passed to the processor's own idempotency support as a second, independent layer.
    Task<SubscriptionIntentResult?> CreateSubscriptionIntentAsync(
        Guid userId, string email, string plan, string idempotencyKey, CancellationToken ct = default);

    /// Tokenizes a future off-session payment method for the customer. Null when not configured.
    Task<SetupIntentResult?> CreateSetupIntentAsync(
        Guid userId, string email, CancellationToken ct = default);

    /// Makes the given payment method the customer's default for future invoices.
    Task SetDefaultPaymentMethodAsync(
        string customerId, string paymentMethodId, CancellationToken ct = default);

    /// Full refund when amountCents is null, partial otherwise. Null if nothing to refund.
    /// idempotencyKey is also passed to the processor's own idempotency support. lastPaymentReference
    /// is the processor-specific id of the subscription's last captured payment (e.g. Adyen's
    /// pspReference) — Stripe/PayPal ignore it and re-derive the latest payment live from their
    /// own subscription object instead.
    Task<RefundResult?> RefundSubscriptionAsync(
        string subscriptionId, long? amountCents, string idempotencyKey,
        string? lastPaymentReference = null, CancellationToken ct = default);

    /// Null when the signature doesn't verify or the payload isn't a recognized event. headers
    /// keys are lower-cased by the caller (different processors sign over different,
    /// differently-named headers — Stripe's is one signature header, PayPal's needs several).
    /// Async because verification itself needs a round-trip for some processors (PayPal calls
    /// a verify-webhook-signature endpoint; Stripe's is purely local HMAC computation).
    Task<PaymentEvent?> VerifyWebhookAsync(
        string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default);

    /// Subscriptions this gateway created on/after `since` — used by reconciliation to repair a
    /// local row whose processor-id link never got saved (e.g. a crash between the gateway call
    /// succeeding and SaveChangesAsync()).
    Task<IReadOnlyList<ReconciliationCandidate>> ListRecentSubscriptionsAsync(
        DateTime since, CancellationToken ct = default);

    /// A hosted self-service session (cancel/upgrade/invoice history) for this customer, if the
    /// processor has one. Null for processors with no portal equivalent (PayPal, Adyen) or when
    /// unconfigured.
    Task<string?> CreatePortalSessionAsync(
        string customerId, string returnUrl, CancellationToken ct = default);
}

public sealed record ReconciliationCandidate(
    string Processor,
    Guid? UserId, // null if the processor-side metadata is missing/unparseable
    string CustomerId,
    string SubscriptionId,
    string? Plan,
    string? ProviderStatus,
    DateTime? PeriodEnd);

public sealed record PaymentEvent(
    string Type,
    string? ClientReferenceId,
    string? CustomerId,
    string? SubscriptionId,
    string? Plan,
    string? ProviderStatus,
    DateTime? PeriodEnd,
    string? PaymentMethodId = null,
    string? EventId = null,
    // Defaults to "stripe" since every event constructed before multi-processor support
    // existed was implicitly Stripe's; new adapters should pass this explicitly.
    string Processor = "stripe",
    // Set for "payment_captured" (Adyen only — the pspReference of the capture, needed since
    // Adyen has no subscription object to look up the latest payment from later).
    string? PaymentReference = null);

public sealed record SubscriptionIntentResult(
    string Processor, string CustomerId, string SubscriptionId, string? ClientSecret, string Status);

public sealed record SetupIntentResult(string Processor, string CustomerId, string ClientSecret);

public sealed record RefundResult(string RefundId, string Status, long? AmountCents);
