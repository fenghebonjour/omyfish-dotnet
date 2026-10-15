using Microsoft.EntityFrameworkCore;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Infrastructure.Persistence;

namespace OMyFish.ShopService.Infrastructure.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly ShopDbContext _db;

    public OrderRepository(ShopDbContext db) => _db = db;

    public Task<Order?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<Order?> FindByStripeSessionIdAsync(string stripeSessionId, CancellationToken ct = default)
        => _db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.StripeSessionId == stripeSessionId, ct);

    public async Task<IReadOnlyList<Order>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await _db.Orders.Include(o => o.Lines).AsNoTracking()
            .Where(o => o.UserId == userId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken ct = default)
        => await _db.Orders.Include(o => o.Lines).AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

    public Task AddAsync(Order order, CancellationToken ct = default)
    {
        _db.Orders.Add(order);
        return Task.CompletedTask;
    }
}
