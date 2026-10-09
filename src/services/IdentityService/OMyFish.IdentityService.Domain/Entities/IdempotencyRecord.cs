namespace OMyFish.IdentityService.Domain.Entities;

/// Reserves a client-supplied Idempotency-Key against one endpoint before the Stripe call
/// runs, then records the result so a retried request with the same key replays it instead
/// of calling Stripe again.
public sealed class IdempotencyRecord
{
    public const string Checkout = "checkout";
    public const string Refund = "refund";

    private IdempotencyRecord() { }

    public Guid Id { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public string Endpoint { get; private set; } = "";
    public Guid UserId { get; private set; }
    public bool Completed { get; private set; }
    public string? CustomerId { get; private set; }
    public string? SubscriptionId { get; private set; }
    public string? ClientSecret { get; private set; }
    public string? CheckoutStatus { get; private set; }
    public string? RefundId { get; private set; }
    public string? RefundStatus { get; private set; }
    public long? AmountCents { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static IdempotencyRecord Reserve(string idempotencyKey, string endpoint, Guid userId)
    {
        return new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            Endpoint = endpoint,
            UserId = userId,
            Completed = false,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public void CompleteCheckout(string customerId, string subscriptionId, string? clientSecret, string status)
    {
        CustomerId = customerId;
        SubscriptionId = subscriptionId;
        ClientSecret = clientSecret;
        CheckoutStatus = status;
        Completed = true;
    }

    public void CompleteRefund(string refundId, string status, long? amountCents)
    {
        RefundId = refundId;
        RefundStatus = status;
        AmountCents = amountCents;
        Completed = true;
    }

    /// True once this reservation has sat uncompleted for longer than maxAge.
    public bool IsStale(TimeSpan maxAge) => CreatedAt < DateTime.UtcNow - maxAge;
}
