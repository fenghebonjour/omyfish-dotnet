using OMyFish.ShopService.Application.Dtos;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Commands;

internal sealed class GetOrCreateCartCommandHandler(ICartRepository cartRepo, IUnitOfWork unitOfWork)
    : ICommandHandler<GetOrCreateCartCommand, CartDto>
{
    public async Task<CartDto> Handle(GetOrCreateCartCommand command, CancellationToken ct)
    {
        var cart = await GetOrCreateAsync(cartRepo, unitOfWork, command.Owner, ct);
        return cart.ToDto();
    }

    internal static async Task<Cart> GetOrCreateAsync(
        ICartRepository cartRepo, IUnitOfWork unitOfWork, Domain.ValueObjects.CartOwner owner, CancellationToken ct)
    {
        var existing = await cartRepo.FindByOwnerAsync(owner, ct);
        if (existing is not null) return existing;

        var cart = Cart.CreateEmpty(owner);
        await cartRepo.AddAsync(cart, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return cart;
    }
}

internal sealed class AddToCartCommandHandler(
    ICartRepository cartRepo, IProductRepository productRepo, IUnitOfWork unitOfWork)
    : ICommandHandler<AddToCartCommand, CartDto>
{
    public async Task<CartDto> Handle(AddToCartCommand command, CancellationToken ct)
    {
        if (await productRepo.FindByIdAsync(command.ProductId, ct) is null)
            throw new ProductNotFoundException(command.ProductId);

        var cart = await GetOrCreateCartCommandHandler.GetOrCreateAsync(cartRepo, unitOfWork, command.Owner, ct);
        cart.AddItem(command.ProductId, command.Quantity);
        await unitOfWork.SaveChangesAsync(ct);
        return cart.ToDto();
    }
}

internal sealed class UpdateCartItemCommandHandler(
    ICartRepository cartRepo, IProductRepository productRepo, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateCartItemCommand, CartDto>
{
    public async Task<CartDto> Handle(UpdateCartItemCommand command, CancellationToken ct)
    {
        if (command.Quantity > 0 && await productRepo.FindByIdAsync(command.ProductId, ct) is null)
            throw new ProductNotFoundException(command.ProductId);

        var cart = await GetOrCreateCartCommandHandler.GetOrCreateAsync(cartRepo, unitOfWork, command.Owner, ct);
        cart.SetQuantity(command.ProductId, command.Quantity);
        await unitOfWork.SaveChangesAsync(ct);
        return cart.ToDto();
    }
}

internal sealed class MergeGuestCartCommandHandler(ICartRepository cartRepo, IUnitOfWork unitOfWork)
    : ICommandHandler<MergeGuestCartCommand, CartDto>
{
    public async Task<CartDto> Handle(MergeGuestCartCommand command, CancellationToken ct)
    {
        var userCart = await GetOrCreateCartCommandHandler.GetOrCreateAsync(
            cartRepo, unitOfWork, Domain.ValueObjects.CartOwner.OfUser(command.UserId), ct);

        var guestCart = await cartRepo.FindByGuestTokenAsync(command.GuestToken, ct);
        if (guestCart is not null)
        {
            userCart.MergeFrom(guestCart);
            await cartRepo.DeleteByGuestTokenAsync(command.GuestToken, ct);
            // Commits the merge and the guest-cart delete together.
            await unitOfWork.SaveChangesAsync(ct);
        }

        return userCart.ToDto();
    }
}
