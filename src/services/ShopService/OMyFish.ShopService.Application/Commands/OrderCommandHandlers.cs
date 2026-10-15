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
    public async Task<Order> Handle(ReserveOrderCommand command, CancellationToken ct)
    {
        var cart = await cartRepo.FindByOwnerAsync(command.Owner, ct) ?? Cart.CreateEmpty(command.Owner);
        cart.RequireNotEmpty();

        var lines = new List<OrderLine>();
        foreach (var item in cart.Items)
        {
            var product = await productRepo.FindByIdAsync(item.ProductId, ct)
                          ?? throw new ProductNotFoundException(item.ProductId);
            product.DecreaseStock(item.Quantity);
            lines.Add(new OrderLine(product.Id, product.Name, product.PriceCents, item.Quantity));
        }

        var order = Order.CreatePendingPayment(
            command.Owner.IsGuest ? null : command.Owner.UserId, command.GuestEmail, lines);
        await orderRepo.AddAsync(order, ct);

        cart.Clear();

        // Commits the order insert, every line's stock decrement, and the cart clear together —
        // the same atomicity Java's @Transactional wrapper around reserve() provided.
        await unitOfWork.SaveChangesAsync(ct);

        return order;
    }
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

        foreach (var line in order.Lines)
        {
            var product = await productRepo.FindByIdAsync(line.ProductId, ct)
                          ?? throw new ProductNotFoundException(line.ProductId);
            product.Restock(line.Quantity);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return true;
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
    IOrderRepository orderRepo, IProductRepository productRepo, IPaymentGateway paymentGateway, IUnitOfWork unitOfWork)
    : ICommandHandler<ApproveReturnCommand, OrderDto>
{
    public async Task<OrderDto> Handle(ApproveReturnCommand command, CancellationToken ct)
    {
        var order = await orderRepo.FindByIdAsync(command.OrderId, ct)
                    ?? throw new OrderNotFoundException(command.OrderId);

        order.ApproveReturn();

        await paymentGateway.RefundAsync(
            order.StripePaymentIntentId, order.TotalCents, command.OrderId.ToString(), ct);

        foreach (var line in order.Lines)
        {
            var product = await productRepo.FindByIdAsync(line.ProductId, ct)
                          ?? throw new ProductNotFoundException(line.ProductId);
            product.Restock(line.Quantity);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return order.ToDto();
    }
}
