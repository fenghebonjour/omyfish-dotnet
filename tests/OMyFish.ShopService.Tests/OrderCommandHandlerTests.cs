using OMyFish.ShopService.Application;
using NSubstitute;
using OMyFish.ShopService.Application.Commands;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.ShopService.Domain.ValueObjects;
using Xunit;

namespace OMyFish.ShopService.Tests;

public class OrderCommandHandlerTests
{
    private readonly ICartRepository _cartRepo = Substitute.For<ICartRepository>();
    private readonly IProductRepository _productRepo = Substitute.For<IProductRepository>();
    private readonly IOrderRepository _orderRepo = Substitute.For<IOrderRepository>();
    private readonly IPaymentGateway _paymentGateway = Substitute.For<IPaymentGateway>();
    private readonly IUnitOfWork _unitOfWork = UnitOfWorkSubstitute.Create();

    [Fact]
    public async Task ReserveOrder_DecreasesStockAndClearsCart()
    {
        var handler = new ReserveOrderCommandHandler(_cartRepo, _productRepo, _orderRepo, _unitOfWork);
        var owner = CartOwner.OfUser(Guid.NewGuid());
        var product = Product.Create("Lure", "desc", 1000, null, stockQuantity: 5, active: true);

        var cart = Cart.CreateEmpty(owner);
        cart.AddItem(product.Id, 2);
        _cartRepo.FindByUserIdAsync(owner.UserId!.Value, Arg.Any<CancellationToken>()).Returns(cart);
        _productRepo.FindByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _productRepo.TryDecreaseStockAsync(product.Id, 2, Arg.Any<CancellationToken>()).Returns(true);

        var order = await handler.Handle(new ReserveOrderCommand(owner, null), CancellationToken.None);

        await _productRepo.Received(1).TryDecreaseStockAsync(product.Id, 2, Arg.Any<CancellationToken>());
        Assert.Empty(cart.Items);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(2000, order.TotalCents);
        await _orderRepo.Received(1).AddAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReserveOrder_AtomicDecrementRejected_ThrowsInsufficientStockWithoutCreatingOrder()
    {
        var handler = new ReserveOrderCommandHandler(_cartRepo, _productRepo, _orderRepo, _unitOfWork);
        var owner = CartOwner.OfUser(Guid.NewGuid());
        // The loaded snapshot says there's enough — the conditional UPDATE is what decides.
        var product = Product.Create("Lure", "desc", 1000, null, stockQuantity: 5, active: true);
        var cart = Cart.CreateEmpty(owner);
        cart.AddItem(product.Id, 2);
        _cartRepo.FindByUserIdAsync(owner.UserId!.Value, Arg.Any<CancellationToken>()).Returns(cart);
        _productRepo.FindByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _productRepo.TryDecreaseStockAsync(product.Id, 2, Arg.Any<CancellationToken>()).Returns(false);

        await Assert.ThrowsAsync<InsufficientStockException>(
            () => handler.Handle(new ReserveOrderCommand(owner, null), CancellationToken.None));

        await _orderRepo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReserveOrder_EmptyCart_ThrowsCartEmpty()
    {
        var handler = new ReserveOrderCommandHandler(_cartRepo, _productRepo, _orderRepo, _unitOfWork);
        var owner = CartOwner.OfUser(Guid.NewGuid());
        _cartRepo.FindByUserIdAsync(owner.UserId!.Value, Arg.Any<CancellationToken>()).Returns((Cart?)null);

        await Assert.ThrowsAsync<CartEmptyException>(
            () => handler.Handle(new ReserveOrderCommand(owner, null), CancellationToken.None));
    }

    [Fact]
    public async Task CancelOrder_RestocksEveryLineAndMarksCancelled()
    {
        var handler = new CancelOrderCommandHandler(_orderRepo, _productRepo, _unitOfWork);
        var product = Product.Create("Lure", "desc", 1000, null, stockQuantity: 3, active: true);
        var order = Order.CreatePendingPayment(
            Guid.NewGuid(), null, [new OrderLine(product.Id, product.Name, product.PriceCents, 2)]);

        _orderRepo.FindByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _productRepo.IncreaseStockAsync(product.Id, 2, Arg.Any<CancellationToken>()).Returns(true);

        await handler.Handle(new CancelOrderCommand(order.Id), CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        await _productRepo.Received(1).IncreaseStockAsync(product.Id, 2, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveReturn_RefundsAndRestocksAndMarksReturned()
    {
        _paymentGateway.Name.Returns("stripe");
        _paymentGateway.IsConfigured.Returns(true);
        var handler = new ApproveReturnCommandHandler(
            _orderRepo, _productRepo, new ShopPaymentProcessorRegistry([_paymentGateway], "stripe"), _unitOfWork);
        var product = Product.Create("Lure", "desc", 1000, null, stockQuantity: 3, active: true);
        var order = Order.CreatePendingPayment(
            Guid.NewGuid(), null, [new OrderLine(product.Id, product.Name, product.PriceCents, 1)]);
        order.MarkPaid("pi_123", null);
        order.RequestReturn("didn't work");

        _orderRepo.FindByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _productRepo.IncreaseStockAsync(product.Id, 1, Arg.Any<CancellationToken>()).Returns(true);

        await handler.Handle(new ApproveReturnCommand(order.Id), CancellationToken.None);

        Assert.Equal(OrderStatus.Returned, order.Status);
        await _productRepo.Received(1).IncreaseStockAsync(product.Id, 1, Arg.Any<CancellationToken>());
        await _paymentGateway.Received(1).RefundAsync(
            "pi_123", order.TotalCents, order.Id.ToString(), Arg.Any<CancellationToken>());
    }
}
