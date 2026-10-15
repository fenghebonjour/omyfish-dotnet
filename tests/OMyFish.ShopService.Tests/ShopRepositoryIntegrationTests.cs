using Microsoft.EntityFrameworkCore;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.ValueObjects;
using OMyFish.ShopService.Infrastructure.Persistence;
using OMyFish.ShopService.Infrastructure.Repositories;
using Xunit;

namespace OMyFish.ShopService.Tests;

// Exercises the repositories/UnitOfWork against a real, migrated Postgres — proves the EF Core
// owned-collection mapping for Cart.Items/Order.Lines (backing-field navigation, see
// ShopDbContext) actually round-trips, not just that it compiles.
[Collection("Postgres")]
public class ShopRepositoryIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Cart_AddAsync_ThenSaveChanges_PersistsItsItems()
    {
        var owner = CartOwner.OfGuest(Guid.NewGuid());
        var cart = Cart.CreateEmpty(owner);
        var productId = Guid.NewGuid();
        cart.AddItem(productId, 3);

        await using (var db = fixture.CreateDbContext())
        {
            await new CartRepository(db).AddAsync(cart);
            await new UnitOfWork(db).SaveChangesAsync();
        }

        await using var verifyDb = fixture.CreateDbContext();
        var saved = await new CartRepository(verifyDb).FindByGuestTokenAsync(owner.GuestToken!.Value);
        Assert.NotNull(saved);
        Assert.Single(saved!.Items);
        Assert.Equal(3, saved.Items[0].Quantity);
    }

    [Fact]
    public async Task Order_AddAsync_ThenMarkPaid_PersistsLinesAndShippingAddress()
    {
        var line = new OrderLine(Guid.NewGuid(), "Lure", 1500, 2);
        var order = Order.CreatePendingPayment(Guid.NewGuid(), null, [line]);
        order.AttachCheckoutSession("sess_abc");

        await using (var db = fixture.CreateDbContext())
        {
            await new OrderRepository(db).AddAsync(order);
            await new UnitOfWork(db).SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            var repo = new OrderRepository(db);
            var reloaded = await repo.FindByStripeSessionIdAsync("sess_abc");
            Assert.NotNull(reloaded);
            reloaded!.MarkPaid("pi_xyz", new ShippingAddress("Jane Doe", "1 Rue Principale", null, "Montreal", "QC", "H3A 1A1", "CA"));
            await new UnitOfWork(db).SaveChangesAsync();
        }

        await using var verifyDb = fixture.CreateDbContext();
        var saved = await verifyDb.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == order.Id);
        Assert.Single(saved.Lines);
        Assert.Equal(1500, saved.Lines[0].UnitPriceCents);
        Assert.Equal(OrderStatus.Paid, saved.Status);
        Assert.Equal("pi_xyz", saved.StripePaymentIntentId);
        Assert.Equal("Montreal", saved.ShippingAddress?.City);
    }
}
