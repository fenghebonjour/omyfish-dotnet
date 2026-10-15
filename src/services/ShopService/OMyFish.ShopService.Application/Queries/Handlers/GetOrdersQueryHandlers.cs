using OMyFish.ShopService.Application.Dtos;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Queries.Handlers;

internal sealed class GetOrdersByUserQueryHandler(IOrderRepository repo)
    : IQueryHandler<GetOrdersByUserQuery, IReadOnlyList<OrderDto>>
{
    public async Task<IReadOnlyList<OrderDto>> Handle(GetOrdersByUserQuery query, CancellationToken ct) =>
        (await repo.GetByUserIdAsync(query.UserId, ct)).Select(o => o.ToDto()).ToList();
}

internal sealed class GetOrderByIdQueryHandler(IOrderRepository repo) : IQueryHandler<GetOrderByIdQuery, OrderDto>
{
    public async Task<OrderDto> Handle(GetOrderByIdQuery query, CancellationToken ct)
    {
        var order = await repo.FindByIdAsync(query.OrderId, ct);
        if (order is null || order.UserId != query.UserId)
            throw new OrderNotFoundException(query.OrderId);
        return order.ToDto();
    }
}

internal sealed class GetAllOrdersForAdminQueryHandler(IOrderRepository repo)
    : IQueryHandler<GetAllOrdersForAdminQuery, IReadOnlyList<OrderDto>>
{
    public async Task<IReadOnlyList<OrderDto>> Handle(GetAllOrdersForAdminQuery query, CancellationToken ct) =>
        (await repo.GetAllAsync(ct)).Select(o => o.ToDto()).ToList();
}

internal sealed class LookupGuestOrderQueryHandler(IOrderRepository repo)
    : IQueryHandler<LookupGuestOrderQuery, OrderDto>
{
    public async Task<OrderDto> Handle(LookupGuestOrderQuery query, CancellationToken ct)
    {
        var order = await repo.FindByIdAsync(query.OrderId, ct);
        if (order?.GuestEmail is null || !order.GuestEmail.Equals(query.Email, StringComparison.OrdinalIgnoreCase))
            throw new OrderNotFoundException(query.OrderId);
        return order.ToDto();
    }
}
