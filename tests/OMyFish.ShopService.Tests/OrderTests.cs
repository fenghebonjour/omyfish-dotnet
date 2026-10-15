using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.ShopService.Domain.ValueObjects;
using Xunit;

namespace OMyFish.ShopService.Tests;

public class OrderTests
{
    private static OrderLine Line(long unitPriceCents = 1000, int quantity = 2) =>
        new(Guid.NewGuid(), "Lure", unitPriceCents, quantity);

    [Fact]
    public void CreatePendingPayment_SumsLineSubtotalsIntoTotal()
    {
        var order = Order.CreatePendingPayment(Guid.NewGuid(), null, [Line(1000, 2), Line(500, 3)]);

        Assert.Equal(1000 * 2 + 500 * 3, order.TotalCents);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
    }

    [Fact]
    public void CreatePendingPayment_WithBothUserIdAndGuestEmail_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Order.CreatePendingPayment(Guid.NewGuid(), "guest@example.com", [Line()]));
    }

    [Fact]
    public void CreatePendingPayment_WithNeitherUserIdNorGuestEmail_Throws()
    {
        Assert.Throws<ArgumentException>(() => Order.CreatePendingPayment(null, null, [Line()]));
    }

    [Fact]
    public void MarkPaid_FromPendingPayment_SetsStatusAndShippingAddress()
    {
        var order = Order.CreatePendingPayment(Guid.NewGuid(), null, [Line()]);
        var address = new ShippingAddress("Jane Doe", "123 Main St", null, "Montreal", "QC", "H3A 1A1", "CA");

        order.MarkPaid("pi_123", address);

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal("pi_123", order.StripePaymentIntentId);
        Assert.Equal(address, order.ShippingAddress);
        Assert.NotNull(order.PaidAt);
    }

    [Fact]
    public void MarkPaid_WhenNotPendingPayment_Throws()
    {
        var order = Order.CreatePendingPayment(Guid.NewGuid(), null, [Line()]);
        order.MarkPaid("pi_123", null);

        Assert.Throws<InvalidOrderStateException>(() => order.MarkPaid("pi_456", null));
    }

    [Fact]
    public void RequestReturn_WithinWindow_Succeeds()
    {
        var order = Order.CreatePendingPayment(Guid.NewGuid(), null, [Line()]);
        order.MarkPaid("pi_123", null);

        order.RequestReturn("wrong size");

        Assert.Equal(OrderStatus.ReturnRequested, order.Status);
        Assert.Equal("wrong size", order.ReturnReason);
    }

    [Fact]
    public void RequestReturn_WhenNotPaid_Throws()
    {
        var order = Order.CreatePendingPayment(Guid.NewGuid(), null, [Line()]);

        Assert.Throws<InvalidOrderStateException>(() => order.RequestReturn("reason"));
    }

    [Fact]
    public void ApproveReturn_FromReturnRequested_Succeeds()
    {
        var order = Order.CreatePendingPayment(Guid.NewGuid(), null, [Line()]);
        order.MarkPaid("pi_123", null);
        order.RequestReturn("reason");

        order.ApproveReturn();

        Assert.Equal(OrderStatus.Returned, order.Status);
        Assert.NotNull(order.ReturnedAt);
    }

    [Fact]
    public void MarkCancelled_FromPendingPayment_Succeeds()
    {
        var order = Order.CreatePendingPayment(Guid.NewGuid(), null, [Line()]);

        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
