namespace OMyFish.ShopService.Infrastructure.Persistence;

/// One row per order line of a consumed OrderPlacedEvent — the replayable audit ledger
/// (OrderEventLedgerConsumer). Persistence-only, not a domain concept.
public sealed class OrderEventLedgerEntry
{
    private OrderEventLedgerEntry() { }

    public OrderEventLedgerEntry(Guid eventId, Guid orderId, Guid productId, string productName, int quantity, long unitPriceCents)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPriceCents = unitPriceCents;
        ReceivedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = "";
    public int Quantity { get; private set; }
    public long UnitPriceCents { get; private set; }
    public DateTime ReceivedAt { get; private set; }
}
