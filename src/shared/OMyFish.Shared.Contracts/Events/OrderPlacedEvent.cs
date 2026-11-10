using System.Text.Json.Serialization;

namespace OMyFish.Shared.Contracts.Events;

/// Published by shop-service on Kafka (not RabbitMQ) once an order's payment is confirmed —
/// not at reservation time, so the confirmation and audit ledger only fire for orders that
/// were actually paid. Same shape as omyfish-java's OrderPlacedEvent.
[method: JsonConstructor]
public record OrderPlacedEvent(
    Guid EventId,
    DateTime OccurredOn,
    Guid OrderId,
    Guid? UserId,
    string? GuestEmail,
    long TotalCents,
    IReadOnlyList<OrderPlacedEvent.Line> Lines)
{
    public OrderPlacedEvent(Guid orderId, Guid? userId, string? guestEmail, long totalCents, IReadOnlyList<Line> lines)
        : this(Guid.NewGuid(), DateTime.UtcNow, orderId, userId, guestEmail, totalCents, lines) { }

    public record Line(Guid ProductId, string ProductName, int Quantity, long UnitPriceCents);

    public const string Topic = "omyfish.shop.orders";

    /// Partitioning by the first line's productId keeps every event for the same hot SKU ordered
    /// through one consumer — the common flash-sale case is a single-product cart; a
    /// multi-line order's later lines don't get that guarantee (accepted at this scale).
    [JsonIgnore]
    public string PartitionKey => Lines[0].ProductId.ToString();
}
