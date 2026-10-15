using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.ShopService.Domain.ValueObjects;
using OMyFish.Shared.BuildingBlocks.Domain;

namespace OMyFish.ShopService.Domain.Entities;

public sealed class Order : AggregateRoot<Guid>
{
    public static readonly TimeSpan ReturnWindow = TimeSpan.FromDays(30);

    private readonly List<OrderLine> _lines = [];

    private Order(Guid id) : base(id) { }

    public Guid? UserId { get; private set; }
    public string? GuestEmail { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
    public long TotalCents { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? StripeSessionId { get; private set; }
    public string? StripePaymentIntentId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public DateTime? ReturnRequestedAt { get; private set; }
    public DateTime? ReturnedAt { get; private set; }
    public string? ReturnReason { get; private set; }
    public ShippingAddress? ShippingAddress { get; private set; }

    public static Order CreatePendingPayment(Guid? userId, string? guestEmail, IReadOnlyList<OrderLine> lines)
    {
        if ((userId is null) == (guestEmail is null))
            throw new ArgumentException("Order requires exactly one of UserId or GuestEmail");

        var order = new Order(Guid.NewGuid())
        {
            UserId = userId,
            GuestEmail = guestEmail,
            TotalCents = lines.Sum(l => l.SubtotalCents),
            Status = OrderStatus.PendingPayment,
            CreatedAt = DateTime.UtcNow
        };
        order._lines.AddRange(lines);
        return order;
    }

    public void AttachCheckoutSession(string stripeSessionId) => StripeSessionId = stripeSessionId;

    public void MarkPaid(string stripePaymentIntentId, ShippingAddress? shippingAddress)
    {
        RequireStatus(OrderStatus.PendingPayment);
        Status = OrderStatus.Paid;
        StripePaymentIntentId = stripePaymentIntentId;
        PaidAt = DateTime.UtcNow;
        ShippingAddress = shippingAddress;
    }

    public void MarkCancelled()
    {
        RequireStatus(OrderStatus.PendingPayment);
        Status = OrderStatus.Cancelled;
    }

    public void RequestReturn(string? reason)
    {
        RequireStatus(OrderStatus.Paid);
        if (PaidAt is null || DateTime.UtcNow > PaidAt.Value.Add(ReturnWindow))
            throw new InvalidOrderStateException(Id, "return window has expired");
        Status = OrderStatus.ReturnRequested;
        ReturnRequestedAt = DateTime.UtcNow;
        ReturnReason = reason;
    }

    public void ApproveReturn()
    {
        RequireStatus(OrderStatus.ReturnRequested);
        Status = OrderStatus.Returned;
        ReturnedAt = DateTime.UtcNow;
    }

    private void RequireStatus(OrderStatus required)
    {
        if (Status != required)
            throw new InvalidOrderStateException(Id, $"expected status {required} but was {Status}");
    }
}
