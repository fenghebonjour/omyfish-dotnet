using NSubstitute;
using OMyFish.ShopService.Application.Commands;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.ShopService.Domain.ValueObjects;
using Xunit;

namespace OMyFish.ShopService.Tests;

public class CartCommandHandlerTests
{
    private readonly ICartRepository _cartRepo = Substitute.For<ICartRepository>();
    private readonly IProductRepository _productRepo = Substitute.For<IProductRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static Product ExistingProduct() => Product.Create("Lure", "desc", 999, null, 10, true);

    [Fact]
    public async Task AddToCart_UnknownProduct_ThrowsProductNotFound()
    {
        var handler = new AddToCartCommandHandler(_cartRepo, _productRepo, _unitOfWork);
        _productRepo.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Product?)null);

        await Assert.ThrowsAsync<ProductNotFoundException>(() => handler.Handle(
            new AddToCartCommand(CartOwner.OfUser(Guid.NewGuid()), Guid.NewGuid(), 1), CancellationToken.None));
    }

    [Fact]
    public async Task AddToCart_KnownProduct_CreatesCartAndAddsItem()
    {
        var handler = new AddToCartCommandHandler(_cartRepo, _productRepo, _unitOfWork);
        var product = ExistingProduct();
        _productRepo.FindByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _cartRepo.FindByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Cart?)null);

        var result = await handler.Handle(
            new AddToCartCommand(CartOwner.OfUser(Guid.NewGuid()), product.Id, 2), CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(2, result.Items[0].Quantity);
        await _cartRepo.Received(1).AddAsync(Arg.Any<Cart>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MergeGuestCart_WhenGuestCartExists_MergesAndDeletesIt()
    {
        var handler = new MergeGuestCartCommandHandler(_cartRepo, _unitOfWork);
        var userId = Guid.NewGuid();
        var guestToken = Guid.NewGuid();
        var sharedProduct = Guid.NewGuid();

        var userCart = Cart.CreateEmpty(CartOwner.OfUser(userId));
        userCart.AddItem(sharedProduct, 1);
        _cartRepo.FindByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(userCart);

        var guestCart = Cart.CreateEmpty(CartOwner.OfGuest(guestToken));
        guestCart.AddItem(sharedProduct, 2);
        _cartRepo.FindByGuestTokenAsync(guestToken, Arg.Any<CancellationToken>()).Returns(guestCart);

        var result = await handler.Handle(new MergeGuestCartCommand(userId, guestToken), CancellationToken.None);

        Assert.Equal(3, result.Items.Single(i => i.ProductId == sharedProduct).Quantity);
        await _cartRepo.Received(1).DeleteByGuestTokenAsync(guestToken, Arg.Any<CancellationToken>());
    }
}
