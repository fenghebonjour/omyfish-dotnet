using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
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
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

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
            PaymentEventType.SessionCompleted, fixture.FakePayment.LastSessionId, "pi_fake", ShippingAddress: null);
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

    [Fact]
    public async Task Checkout_RetriedWithSameIdempotencyKey_ReplaysTheFirstOrder()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Cart-Token", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await client.PostAsJsonAsync(
            "/api/v1/shop/cart/items", new { productId = SeededProductId, quantity = 1 })).EnsureSuccessStatusCode();
        var body = new
        {
            successUrl = "https://frontend.test/success",
            cancelUrl = "https://frontend.test/cancel",
            guestEmail = "guest@example.com",
        };

        var first = await client.PostAsJsonAsync("/api/v1/shop/checkout", body);
        // The cart is now empty — without the key, a retry would 400 (or, with items, double-order).
        var retry = await client.PostAsJsonAsync("/api/v1/shop/checkout", body);

        first.EnsureSuccessStatusCode();
        retry.EnsureSuccessStatusCode();
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetGuid();
        var retryId = (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetGuid();
        Assert.Equal(firstId, retryId);
    }

    [Fact]
    public async Task Checkout_WithoutIdempotencyKey_Returns400()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Cart-Token", Guid.NewGuid().ToString());

        var response = await client.PostAsJsonAsync("/api/v1/shop/checkout", new
        {
            successUrl = "https://frontend.test/success",
            cancelUrl = "https://frontend.test/cancel",
            guestEmail = "guest@example.com",
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StaleOrderSweep_CancelsAbandonedPendingOrderAndRestocks()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Cart-Token", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await client.PostAsJsonAsync(
            "/api/v1/shop/cart/items", new { productId = SeededProductId, quantity = 1 })).EnsureSuccessStatusCode();

        int StockNow()
        {
            using var db = fixture.CreateDbContext();
            return db.Products.AsNoTracking().Single(p => p.Id == SeededProductId).StockQuantity;
        }

        var stockBefore = StockNow();
        var checkout = await client.PostAsJsonAsync("/api/v1/shop/checkout", new
        {
            successUrl = "https://frontend.test/success",
            cancelUrl = "https://frontend.test/cancel",
            guestEmail = "guest@example.com",
        });
        checkout.EnsureSuccessStatusCode();
        var orderId = (await checkout.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetGuid();
        Assert.Equal(stockBefore - 1, StockNow());

        var sweep = fixture.Services.GetServices<IHostedService>().OfType<StaleOrderSweepService>().Single();
        // A cutoff in the future makes every pending order "stale" — no clock faking needed.
        await sweep.SweepAsync(DateTime.UtcNow.AddMinutes(1), CancellationToken.None);

        await using var verifyDb = fixture.CreateDbContext();
        var order = await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(stockBefore, StockNow());
    }

    [Fact]
    public async Task Checkout_WhileKillSwitchOff_Returns503WithoutReserving()
    {
        var flags = fixture.Services.GetRequiredService<FeatureFlags>();
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Cart-Token", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await client.PostAsJsonAsync(
            "/api/v1/shop/cart/items", new { productId = SeededProductId, quantity = 1 })).EnsureSuccessStatusCode();

        flags.CheckoutEnabled = false;
        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/shop/checkout", new
            {
                successUrl = "https://frontend.test/success",
                cancelUrl = "https://frontend.test/cancel",
                guestEmail = "guest@example.com",
            });

            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var cart = await client.GetFromJsonAsync<JsonElement>("/api/v1/shop/cart");
            Assert.Equal(1, cart.GetProperty("items").GetArrayLength()); // nothing reserved
        }
        finally
        {
            flags.CheckoutEnabled = true;
        }
    }

    [Fact]
    public async Task KillSwitchEndpoint_WithoutAdminToken_Returns401()
    {
        var response = await fixture.CreateClient().PostAsJsonAsync(
            "/api/v1/shop/admin/feature-flags/checkout", new { enabled = false });

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(fixture.Services.GetRequiredService<FeatureFlags>().CheckoutEnabled);
    }
}
