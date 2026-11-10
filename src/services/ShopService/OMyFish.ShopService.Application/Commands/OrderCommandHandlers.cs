using OMyFish.ShopService.Application;
using OMyFish.ShopService.Application.Dtos;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.ShopService.Domain.ValueObjects;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Commands;

internal sealed class ReserveOrderCommandHandler(
    ICartRepository cartRepo, IProductRepository productRepo, IOrderRepository orderRepo, IUnitOfWork unitOfWork)
    : ICommandHandler<ReserveOrderCommand, Order>
{
    public Task<Order> Handle(ReserveOrderCommand command, CancellationToken ct) =>
        // One transaction for the order insert, every line's stock decrement, and the cart
        // clear — the same atomicity Java's @Transactional wrapper around reserve() provides.
        unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var cart = await cartRepo.FindByOwnerAsync(command.Owner, ct) ?? Cart.CreateEmpty(command.Owner);
            cart.RequireNotEmpty();

            var lines = new List<OrderLine>();
            foreach (var item in cart.Items)
            {
                var product = await productRepo.FindByIdAsync(item.ProductId, ct)
                              ?? throw new ProductNotFoundException(item.ProductId);
                // product.StockQuantity is only a snapshot for the error message — the atomic
                // conditional UPDATE is the authority on whether enough stock remains, so a stale
                // read between this load and the UPDATE can't oversell.
                if (!await productRepo.TryDecreaseStockAsync(product.Id, item.Quantity, ct))
                    throw new InsufficientStockException(product.Id, item.Quantity, product.StockQuantity);
                lines.Add(new OrderLine(product.Id, product.Name, product.PriceCents, item.Quantity));
            }

            var order = Order.CreatePendingPayment(
                command.Owner.IsGuest ? null : command.Owner.UserId, command.GuestEmail, lines);
            await orderRepo.AddAsync(order, ct);

            cart.Clear();
            await unitOfWork.SaveChangesAsync(ct);
            return order;
        }, ct);
}

internal sealed class CancelOrderCommandHandler(
    IOrderRepository orderRepo, IProductRepository productRepo, IUnitOfWork unitOfWork)
    : ICommandHandler<CancelOrderCommand, bool>
{
    public async Task<bool> Handle(CancelOrderCommand command, CancellationToken ct)
    {
        var order = await orderRepo.FindByIdAsync(command.OrderId, ct)
                    ?? throw new OrderNotFoundException(command.OrderId);
        order.MarkCancelled();

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await RestockLinesAsync(productRepo, order, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    internal static async Task RestockLinesAsync(IProductRepository productRepo, Order order, CancellationToken ct)
    {
        foreach (var line in order.Lines)
            if (!await productRepo.IncreaseStockAsync(line.ProductId, line.Quantity, ct))
                throw new ProductNotFoundException(line.ProductId);
    }
}

internal sealed class RequestReturnCommandHandler(IOrderRepository orderRepo, IUnitOfWork unitOfWork)
    : ICommandHandler<RequestReturnCommand, OrderDto>
{
    public async Task<OrderDto> Handle(RequestReturnCommand command, CancellationToken ct)
    {
        var order = await orderRepo.FindByIdAsync(command.OrderId, ct);
        if (order is null || order.UserId != command.UserId)
            throw new OrderNotFoundException(command.OrderId);

        order.RequestReturn(command.Reason);
        await unitOfWork.SaveChangesAsync(ct);
        return order.ToDto();
    }
}

internal sealed class ApproveReturnCommandHandler(
    IOrderRepository orderRepo, IProductRepository productRepo, ShopPaymentProcessorRegistry processors,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ApproveReturnCommand, OrderDto>
{
    public async Task<OrderDto> Handle(ApproveReturnCommand command, CancellationToken ct)
    {
        var order = await orderRepo.FindByIdAsync(command.OrderId, ct)
                    ?? throw new OrderNotFoundException(command.OrderId);

        order.ApproveReturn();

        // Orders from before multi-processor support have no PaymentProcessor; they were Stripe.
        await processors.ByName(order.PaymentProcessor ?? "stripe").RefundAsync(
            order.StripePaymentIntentId, order.TotalCents, command.OrderId.ToString(), ct);

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await CancelOrderCommandHandler.RestockLinesAsync(productRepo, order, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return order.ToDto();
        }, ct);
    }
}
