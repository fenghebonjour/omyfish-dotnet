using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.Exceptions;
using Xunit;

namespace OMyFish.ShopService.Tests;

public class ProductTests
{
    [Fact]
    public void DecreaseStock_WithEnoughStock_ReducesQuantity()
    {
        var product = Product.Create("Lure", "desc", 999, null, stockQuantity: 10, active: true);

        product.DecreaseStock(3);

        Assert.Equal(7, product.StockQuantity);
    }

    [Fact]
    public void DecreaseStock_MoreThanAvailable_ThrowsInsufficientStock()
    {
        var product = Product.Create("Lure", "desc", 999, null, stockQuantity: 2, active: true);

        Assert.Throws<InsufficientStockException>(() => product.DecreaseStock(3));
    }

    [Fact]
    public void Restock_IncreasesQuantity()
    {
        var product = Product.Create("Lure", "desc", 999, null, stockQuantity: 2, active: true);

        product.Restock(5);

        Assert.Equal(7, product.StockQuantity);
    }

    [Fact]
    public void SetImage_UpdatesUrlAndStorageKey()
    {
        var product = Product.Create("Lure", "desc", 999, null, stockQuantity: 2, active: true);

        product.SetImage("/api/v1/shop/products/x/image", "products/key.jpg");

        Assert.Equal("/api/v1/shop/products/x/image", product.ImageUrl);
        Assert.Equal("products/key.jpg", product.ImageStorageKey);
    }
}
