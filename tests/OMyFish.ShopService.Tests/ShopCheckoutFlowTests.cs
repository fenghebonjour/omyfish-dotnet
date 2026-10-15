using System.Net.Http.Json;
using System.Text.Json;
using OMyFish.ShopService.Application.Interfaces;
using Xunit;

namespace OMyFish.ShopService.Tests;

// Endpoint-level happy-path checkout flow: guest browses the seeded catalog, adds an item,
// checks out (Stripe faked via ShopApiFixture.FakePayment), the webhook confirms payment, and
// the order comes back PAID via the guest order-lookup endpoint — mirroring Java
// shop-service's ShopCheckoutFlowIT.
[Collection("ShopApi")]
public class ShopCheckoutFlowTests(ShopApiFixture fixture)
{
    private static readonly Guid SeededProductId = Guid.Parse("11111111-1111-1111-1111-111111111001");

    [Fact]
    public async Task GuestCheckout_ThenWebhook_MarksOrderPaid()
    {
        var client = fixture.CreateClient();
        var cartToken = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Cart-Token", cartToken.ToString());

        var productsResponse = await client.GetAsync("/api/v1/shop/products");
        Assert.True(productsResponse.IsSuccessStatusCode);

        var addItemResponse = await client.PostAsJsonAsync(
            "/api/v1/shop/cart/items", new { productId = SeededProductId, quantity = 1 });
        Assert.True(addItemResponse.IsSuccessStatusCode);

        var checkoutResponse = await client.PostAsJsonAsync("/api/v1/shop/checkout", new
        {
            successUrl = "https://frontend.test/success",
            cancelUrl = "https://frontend.test/cancel",
            guestEmail = "guest@example.com",
        });
        Assert.True(checkoutResponse.IsSuccessStatusCode);
        var checkoutBody = await checkoutResponse.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = checkoutBody.GetProperty("orderId").GetGuid();
        Assert.Equal(fixture.FakePayment.NextCheckoutUrl, checkoutBody.GetProperty("checkoutUrl").GetString());

        fixture.FakePayment.NextWebhookEvent = new PaymentEvent(
            PaymentEventType.SessionCompleted, fixture.FakePayment.NextSessionId, "pi_fake", ShippingAddress: null);
        var webhookResponse = await client.PostAsync(
            "/api/v1/shop/webhook/stripe", new StringContent("{}"));
        Assert.True(webhookResponse.IsSuccessStatusCode);
        var webhookBody = await webhookResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(webhookBody.GetProperty("handled").GetBoolean());

        var lookupResponse = await client.GetAsync($"/api/v1/shop/orders/lookup?orderId={orderId}&email=guest@example.com");
        Assert.True(lookupResponse.IsSuccessStatusCode);
        var order = await lookupResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)OMyFish.ShopService.Domain.Entities.OrderStatus.Paid, order.GetProperty("status").GetInt32());
    }
}
