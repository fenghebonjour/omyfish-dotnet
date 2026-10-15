using OMyFish.ShopService.Application.Dtos;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.ValueObjects;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Commands;

// Classified as a command (not a query) per this repo's CQRS rule — "writes are commands" —
// since it may persist a brand-new empty cart, even though the HTTP verb behind it is GET.
public sealed record GetOrCreateCartCommand(CartOwner Owner) : ICommand<CartDto>;

public sealed record AddToCartCommand(CartOwner Owner, Guid ProductId, int Quantity) : ICommand<CartDto>;

public sealed record UpdateCartItemCommand(CartOwner Owner, Guid ProductId, int Quantity) : ICommand<CartDto>;

// Authenticated only — folds the guest cart named by GuestToken into the caller's account
// cart. Called by the frontend right after login/register when a guest cart token is still
// in storage.
public sealed record MergeGuestCartCommand(Guid UserId, Guid GuestToken) : ICommand<CartDto>;

internal static class CartOwnerLookup
{
    public static Task<Domain.Entities.Cart?> FindByOwnerAsync(
        this ICartRepository repo, CartOwner owner, CancellationToken ct) =>
        owner.IsGuest ? repo.FindByGuestTokenAsync(owner.GuestToken!.Value, ct) : repo.FindByUserIdAsync(owner.UserId!.Value, ct);
}
