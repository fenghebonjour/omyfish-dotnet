using OMyFish.ShopService.Application.Dtos;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.ValueObjects;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Commands;

// Internal composition step (not exposed over HTTP directly — see CheckoutCommand). Returns
// the domain entity, not a DTO: CheckoutCommandHandler needs the still-tracked Order to attach
// a Stripe session id to it afterwards without a redundant re-fetch.
public sealed record ReserveOrderCommand(CartOwner Owner, string? GuestEmail) : ICommand<Order>;

// Internal composition step (not exposed over HTTP directly). Restocks every line and marks
// the order cancelled — used both as checkout's Stripe-failure compensation and as the
// webhook's SessionExpired handling.
public sealed record CancelOrderCommand(Guid OrderId) : ICommand<bool>;

public sealed record RequestReturnCommand(Guid OrderId, Guid UserId, string? Reason) : ICommand<OrderDto>;

public sealed record ApproveReturnCommand(Guid OrderId) : ICommand<OrderDto>;
