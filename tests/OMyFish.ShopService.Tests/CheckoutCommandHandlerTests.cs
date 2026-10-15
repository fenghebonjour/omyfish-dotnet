using MediatR;
using NSubstitute;
using OMyFish.ShopService.Application.Commands;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.ValueObjects;
using Xunit;

namespace OMyFish.ShopService.Tests;

public class CheckoutCommandHandlerTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IPaymentGateway _paymentGateway = Substitute.For<IPaymentGateway>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CheckoutCommandHandler _handler;

    public CheckoutCommandHandlerTests()
    {
        _handler = new CheckoutCommandHandler(_sender, _paymentGateway, _unitOfWork);
        _paymentGateway.IsConfigured.Returns(true);
    }

    private static Order ReservedOrder() =>
        Order.CreatePendingPayment(Guid.NewGuid(), null, [new OrderLine(Guid.NewGuid(), "Lure", 1000, 1)]);

    [Fact]
    public async Task Handle_NoPaymentProcessorConfigured_ThrowsBeforeReserving()
    {
        _paymentGateway.IsConfigured.Returns(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.Handle(new CheckoutCommand(CartOwner.OfUser(Guid.NewGuid()), null, "ok", "cancel"), CancellationToken.None));

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
            new CheckoutCommand(CartOwner.OfUser(Guid.NewGuid()), null, "ok", "cancel"), CancellationToken.None);

        Assert.Equal(order.Id, result.OrderId);
        Assert.Equal("https://stripe.test/checkout/sess_123", result.CheckoutUrl);
        Assert.Equal("sess_123", order.StripeSessionId);
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
            new CheckoutCommand(CartOwner.OfUser(Guid.NewGuid()), null, "ok", "cancel"), CancellationToken.None));

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
            new CheckoutCommand(CartOwner.OfUser(Guid.NewGuid()), null, "ok", "cancel"), CancellationToken.None));

        await _sender.Received(1).Send(
            Arg.Is<CancelOrderCommand>(c => c.OrderId == order.Id), Arg.Any<CancellationToken>());
    }
}
