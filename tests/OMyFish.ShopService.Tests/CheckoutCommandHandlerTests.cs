using OMyFish.ShopService.Application;
using MediatR;
using NSubstitute;
using OMyFish.ShopService.Application.Commands;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.ShopService.Domain.ValueObjects;
using Xunit;

namespace OMyFish.ShopService.Tests;

public class CheckoutCommandHandlerTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IPaymentGateway _paymentGateway = Substitute.For<IPaymentGateway>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IIdempotencyKeyRepository _idempotencyKeys = Substitute.For<IIdempotencyKeyRepository>();
    private readonly IOrderEventPublisher _orderEvents = Substitute.For<IOrderEventPublisher>();
    private readonly CheckoutCommandHandler _handler;

    private const string Key = "idem-1";

    public CheckoutCommandHandlerTests()
    {
        _paymentGateway.Name.Returns("stripe");
        _paymentGateway.IsConfigured.Returns(true);
        _handler = new CheckoutCommandHandler(
            _sender, new ShopPaymentProcessorRegistry([_paymentGateway], "stripe"), _unitOfWork, _idempotencyKeys);
        // No existing reservation by default; ReserveAsync hands back a fresh record.
        _idempotencyKeys.ReserveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => IdempotencyRecord.Reserve((string)ci[0], (string)ci[1], (string)ci[2]));
    }

    private static CheckoutCommand Command(Guid userId) =>
        new(CartOwner.OfUser(userId), null, "ok", "cancel", Key);

    private static Order ReservedOrder() =>
        Order.CreatePendingPayment(Guid.NewGuid(), null, [new OrderLine(Guid.NewGuid(), "Lure", 1000, 1)]);

    [Fact]
    public async Task Handle_NoPaymentProcessorConfigured_ThrowsBeforeReserving()
    {
        _paymentGateway.IsConfigured.Returns(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.Handle(Command(Guid.NewGuid()), CancellationToken.None));

        await _sender.DidNotReceive().Send(Arg.Any<ReserveOrderCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HappyPath_AttachesSessionAndSaves()
    {
        var order = ReservedOrder();
        _sender.Send(Arg.Any<ReserveOrderCommand>(), Arg.Any<CancellationToken>()).Returns(order);
        _paymentGateway.CreateCheckoutSessionAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyList<PaymentLineItem>>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new CheckoutSession("sess_123", "https://stripe.test/checkout/sess_123"));

        var result = await _handler.Handle(
            Command(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(order.Id, result.OrderId);
        Assert.Equal("https://stripe.test/checkout/sess_123", result.CheckoutUrl);
        Assert.Equal("sess_123", order.StripeSessionId);
        Assert.Equal("stripe", order.PaymentProcessor);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _sender.DidNotReceive().Send(Arg.Any<CancelOrderCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StripeSessionCreationThrows_CompensatesByCancellingOrder()
    {
        var order = ReservedOrder();
        _sender.Send(Arg.Any<ReserveOrderCommand>(), Arg.Any<CancellationToken>()).Returns(order);
        _paymentGateway.CreateCheckoutSessionAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyList<PaymentLineItem>>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<CheckoutSession?>>(_ => throw new InvalidOperationException("stripe down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(
            Command(Guid.NewGuid()), CancellationToken.None));

        await _sender.Received(1).Send(
            Arg.Is<CancelOrderCommand>(c => c.OrderId == order.Id), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoSessionReturned_CompensatesByCancellingOrder()
    {
        var order = ReservedOrder();
        _sender.Send(Arg.Any<ReserveOrderCommand>(), Arg.Any<CancellationToken>()).Returns(order);
        _paymentGateway.CreateCheckoutSessionAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyList<PaymentLineItem>>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((CheckoutSession?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(
            Command(Guid.NewGuid()), CancellationToken.None));

        await _sender.Received(1).Send(
            Arg.Is<CancelOrderCommand>(c => c.OrderId == order.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CompletedKeyReplayedBySameOwner_ReturnsFirstResultWithoutReserving()
    {
        var userId = Guid.NewGuid();
        var record = IdempotencyRecord.Reserve(Key, IdempotencyRecord.Checkout, $"user:{userId}");
        var orderId = Guid.NewGuid();
        record.CompleteCheckout(orderId, "https://stripe.test/first");
        _idempotencyKeys.FindAsync(Key, IdempotencyRecord.Checkout, Arg.Any<CancellationToken>()).Returns(record);

        var result = await _handler.Handle(Command(userId), CancellationToken.None);

        Assert.Equal(orderId, result.OrderId);
        Assert.Equal("https://stripe.test/first", result.CheckoutUrl);
        await _sender.DidNotReceive().Send(Arg.Any<ReserveOrderCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_KeyStillInProgress_ThrowsConflict()
    {
        var userId = Guid.NewGuid();
        _idempotencyKeys.FindAsync(Key, IdempotencyRecord.Checkout, Arg.Any<CancellationToken>())
            .Returns(IdempotencyRecord.Reserve(Key, IdempotencyRecord.Checkout, $"user:{userId}"));

        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => _handler.Handle(Command(userId), CancellationToken.None));
        await _sender.DidNotReceive().Send(Arg.Any<ReserveOrderCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_KeyReplayedByDifferentOwner_ThrowsArgumentException()
    {
        var record = IdempotencyRecord.Reserve(Key, IdempotencyRecord.Checkout, $"user:{Guid.NewGuid()}");
        record.CompleteCheckout(Guid.NewGuid(), "https://stripe.test/someone-else");
        _idempotencyKeys.FindAsync(Key, IdempotencyRecord.Checkout, Arg.Any<CancellationToken>()).Returns(record);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.Handle(Command(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_CheckoutFails_ReleasesTheReservationSoTheKeyCanBeRetried()
    {
        _sender.Send(Arg.Any<ReserveOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<Order>>(_ => throw new CartEmptyException(null, Guid.NewGuid()));

        await Assert.ThrowsAnyAsync<Exception>(() => _handler.Handle(Command(Guid.NewGuid()), CancellationToken.None));

        await _idempotencyKeys.Received(1).DeleteAsync(Arg.Any<IdempotencyRecord>(), Arg.Any<CancellationToken>());
    }

    // --- webhook routing by processor ---

    private static IPaymentGateway Gateway(string name)
    {
        var g = Substitute.For<IPaymentGateway>();
        g.Name.Returns(name);
        g.IsConfigured.Returns(true);
        return g;
    }

    private static Order PendingOrderOwnedBy(string processor)
    {
        var order = ReservedOrder();
        order.AttachCheckoutSession("sess_abc", processor);
        return order;
    }

    [Fact]
    public async Task Webhook_UnknownProcessor_ReturnsFalseWithoutVerifying()
    {
        var stripe = Gateway("stripe");
        var orders = Substitute.For<IOrderRepository>();
        var handler = new ConfirmPaymentWebhookCommandHandler(
            new ShopPaymentProcessorRegistry([stripe], "stripe"), orders, _sender, _unitOfWork, _orderEvents);

        var handled = await handler.Handle(
            new ConfirmPaymentWebhookCommand("bogus", "{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.False(handled);
        await stripe.DidNotReceiveWithAnyArgs().VerifyWebhookAsync(default!, default!, default);
    }

    [Fact]
    public async Task Webhook_EventForOrderOwnedByAnotherProcessor_IsIgnored()
    {
        var paypal = Gateway("paypal");
        var order = PendingOrderOwnedBy("stripe");
        paypal.VerifyWebhookAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentEvent(PaymentEventType.SessionCompleted, "sess_abc", "cap_1", null));
        var orders = Substitute.For<IOrderRepository>();
        orders.FindByStripeSessionIdAsync("sess_abc", Arg.Any<CancellationToken>()).Returns(order);
        var handler = new ConfirmPaymentWebhookCommandHandler(
            new ShopPaymentProcessorRegistry([Gateway("stripe"), paypal], "stripe"), orders, _sender, _unitOfWork, _orderEvents);

        var handled = await handler.Handle(
            new ConfirmPaymentWebhookCommand("paypal", "{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.False(handled);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        _orderEvents.DidNotReceiveWithAnyArgs().PublishOrderPlaced(default!);
    }

    [Fact]
    public async Task Webhook_EventCarryingOrderId_LooksUpByIdAndMarksPaid()
    {
        // Adyen's AUTHORISATION only echoes our merchantReference (the order id), not the session id.
        var adyen = Gateway("adyen");
        var order = PendingOrderOwnedBy("adyen");
        adyen.VerifyWebhookAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentEvent(PaymentEventType.SessionCompleted, "", "psp_1", null, order.Id));
        var orders = Substitute.For<IOrderRepository>();
        orders.FindByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var handler = new ConfirmPaymentWebhookCommandHandler(
            new ShopPaymentProcessorRegistry([adyen], "stripe"), orders, _sender, _unitOfWork, _orderEvents);

        var handled = await handler.Handle(
            new ConfirmPaymentWebhookCommand("adyen", "{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal("psp_1", order.StripePaymentIntentId);
        // Published only once payment is confirmed, never at reservation time.
        _orderEvents.Received(1).PublishOrderPlaced(order);
    }
}
