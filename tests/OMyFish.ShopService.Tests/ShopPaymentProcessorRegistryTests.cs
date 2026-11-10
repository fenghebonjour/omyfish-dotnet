using NSubstitute;
using OMyFish.ShopService.Application;
using OMyFish.ShopService.Application.Interfaces;
using Xunit;

namespace OMyFish.ShopService.Tests;

public class ShopPaymentProcessorRegistryTests
{
    private static IPaymentGateway Gateway(string name, bool configured)
    {
        var g = Substitute.For<IPaymentGateway>();
        g.Name.Returns(name);
        g.IsConfigured.Returns(configured);
        return g;
    }

    [Fact]
    public void DefaultProcessor_PreferredIsConfigured_ReturnsIt()
    {
        var paypal = Gateway("paypal", true);
        var registry = new ShopPaymentProcessorRegistry([Gateway("stripe", true), paypal], "paypal");

        Assert.Same(paypal, registry.DefaultProcessor());
    }

    [Fact]
    public void DefaultProcessor_PreferredNotConfigured_FallsBackToFirstConfigured()
    {
        var adyen = Gateway("adyen", true);
        var registry = new ShopPaymentProcessorRegistry(
            [Gateway("stripe", false), Gateway("paypal", false), adyen], "stripe");

        Assert.Same(adyen, registry.DefaultProcessor());
    }

    [Fact]
    public void DefaultProcessor_NoneConfigured_ReturnsNull()
    {
        var registry = new ShopPaymentProcessorRegistry([Gateway("stripe", false)], "stripe");

        Assert.Null(registry.DefaultProcessor());
    }

    [Fact]
    public void ByName_UnconfiguredOrUnknown_Throws()
    {
        var registry = new ShopPaymentProcessorRegistry([Gateway("stripe", false)], "stripe");

        Assert.Throws<InvalidOperationException>(() => registry.ByName("stripe"));
        Assert.Throws<InvalidOperationException>(() => registry.ByName("paypal"));
        Assert.Null(registry.TryByName("paypal"));
    }
}
