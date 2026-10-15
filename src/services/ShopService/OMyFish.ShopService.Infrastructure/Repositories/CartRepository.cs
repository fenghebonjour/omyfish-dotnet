using Microsoft.EntityFrameworkCore;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Infrastructure.Persistence;

namespace OMyFish.ShopService.Infrastructure.Repositories;

public class CartRepository : ICartRepository
{
    private readonly ShopDbContext _db;

    public CartRepository(ShopDbContext db) => _db = db;

    public Task<Cart?> FindByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId, ct);

    public Task<Cart?> FindByGuestTokenAsync(Guid guestToken, CancellationToken ct = default)
        => _db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.GuestToken == guestToken, ct);

    public Task AddAsync(Cart cart, CancellationToken ct = default)
    {
        _db.Carts.Add(cart);
        return Task.CompletedTask;
    }

    // Stages the delete only — the caller commits via IUnitOfWork so this can share one
    // transaction with whatever else it's doing (e.g. MergeGuestCartCommandHandler's merge).
    public async Task DeleteByGuestTokenAsync(Guid guestToken, CancellationToken ct = default)
    {
        var cart = await _db.Carts.FirstOrDefaultAsync(c => c.GuestToken == guestToken, ct);
        if (cart is not null)
            _db.Carts.Remove(cart);
    }
}
