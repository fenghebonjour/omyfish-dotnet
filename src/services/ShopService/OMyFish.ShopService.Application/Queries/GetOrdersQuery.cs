using OMyFish.ShopService.Application.Dtos;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Queries;

public sealed record GetOrdersByUserQuery(Guid UserId) : IQuery<IReadOnlyList<OrderDto>>;

public sealed record GetOrderByIdQuery(Guid OrderId, Guid UserId) : IQuery<OrderDto>;

// Admin-only — lists every order regardless of owner (newest first).
public sealed record GetAllOrdersForAdminQuery : IQuery<IReadOnlyList<OrderDto>>;

// Guest order tracking by orderId + email, no account needed.
public sealed record LookupGuestOrderQuery(Guid OrderId, string Email) : IQuery<OrderDto>;
