using OMyFish.ShopService.Domain.Entities;
using Xunit;

namespace OMyFish.ShopService.Tests;

public class ProductTests
{
    [Fact]
    public void SetImage_UpdatesUrlAndStorageKey()
    {
        var product = Product.Create("Lure", "desc", 999, null, stockQuantity: 2, active: true);

        product.SetImage("/api/v1/shop/products/x/image", "products/key.jpg");

        Assert.Equal("/api/v1/shop/products/x/image", product.ImageUrl);
        Assert.Equal("products/key.jpg", product.ImageStorageKey);
    }
}
