using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.ShopService.Domain.ValueObjects;
using Xunit;

namespace OMyFish.ShopService.Tests;

public class CartTests
{
    [Fact]
    public void AddItem_NewProduct_AddsIt()
    {
        var cart = Cart.CreateEmpty(CartOwner.OfUser(Guid.NewGuid()));
        var productId = Guid.NewGuid();

        cart.AddItem(productId, 2);

        Assert.Single(cart.Items);
        Assert.Equal(2, cart.Items[0].Quantity);
    }

    [Fact]
    public void AddItem_ExistingProduct_SumsQuantity()
    {
        var cart = Cart.CreateEmpty(CartOwner.OfUser(Guid.NewGuid()));
        var productId = Guid.NewGuid();
        cart.AddItem(productId, 2);

        cart.AddItem(productId, 3);

        Assert.Single(cart.Items);
        Assert.Equal(5, cart.Items[0].Quantity);
    }

    [Fact]
    public void SetQuantity_ToZero_RemovesItem()
    {
        var cart = Cart.CreateEmpty(CartOwner.OfUser(Guid.NewGuid()));
        var productId = Guid.NewGuid();
        cart.AddItem(productId, 2);

        cart.SetQuantity(productId, 0);

        Assert.Empty(cart.Items);
    }

    [Fact]
    public void MergeFrom_SumsOverlappingProductsAndAddsNewOnes()
    {
        var shared = Guid.NewGuid();
        var guestOnly = Guid.NewGuid();
        var userCart = Cart.CreateEmpty(CartOwner.OfUser(Guid.NewGuid()));
        userCart.AddItem(shared, 1);
        var guestCart = Cart.CreateEmpty(CartOwner.OfGuest(Guid.NewGuid()));
        guestCart.AddItem(shared, 2);
        guestCart.AddItem(guestOnly, 1);

        userCart.MergeFrom(guestCart);

        Assert.Equal(3, userCart.Items.First(i => i.ProductId == shared).Quantity);
        Assert.Equal(1, userCart.Items.First(i => i.ProductId == guestOnly).Quantity);
    }

    [Fact]
    public void RequireNotEmpty_OnEmptyCart_Throws()
    {
        var cart = Cart.CreateEmpty(CartOwner.OfUser(Guid.NewGuid()));

        Assert.Throws<CartEmptyException>(() => cart.RequireNotEmpty());
    }

    [Fact]
    public void CartOwner_OfGuest_IsGuestIsTrue()
    {
        var owner = CartOwner.OfGuest(Guid.NewGuid());

        Assert.True(owner.IsGuest);
    }

    [Fact]
    public void CartOwner_OfUser_IsGuestIsFalse()
    {
        var owner = CartOwner.OfUser(Guid.NewGuid());

        Assert.False(owner.IsGuest);
    }
}
