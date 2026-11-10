namespace OMyFish.ShopService.Domain.Entities;

/// Reserves a client-supplied Idempotency-Key against one endpoint before checkout runs, then
/// records the result so a retried request with the same key replays it instead of reserving
/// stock and creating an order a second time — same role as IdentityService's IdempotencyRecord
/// for billing checkout, adapted to Omyshop's order/checkout-url shape.
public sealed class IdempotencyRecord
{
    public const string Checkout = "checkout";

    private IdempotencyRecord() { }

    public Guid Id { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public string Endpoint { get; private set; } = "";
    public string OwnerKey { get; private set; } = "";
    public bool Completed { get; private set; }
    public Guid? OrderId { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static IdempotencyRecord Reserve(string idempotencyKey, string endpoint, string ownerKey) => new()
    {
        Id = Guid.NewGuid(),
        IdempotencyKey = idempotencyKey,
        Endpoint = endpoint,
        OwnerKey = ownerKey,
        CreatedAt = DateTime.UtcNow,
    };

    public void CompleteCheckout(Guid orderId, string checkoutUrl)
    {
        OrderId = orderId;
        CheckoutUrl = checkoutUrl;
        Completed = true;
    }

    /// True once this reservation has sat uncompleted for longer than maxAge.
    public bool IsStale(TimeSpan maxAge) => CreatedAt < DateTime.UtcNow - maxAge;
}
