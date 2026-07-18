using Microsoft.EntityFrameworkCore;
using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Domain.Interfaces;
using OMyFish.IdentityService.Infrastructure.Persistence;

namespace OMyFish.IdentityService.Infrastructure.Repositories;

public class SubscriptionRepository : ISubscriptionRepository
{
    private readonly IdentityDbContext _db;

    public SubscriptionRepository(IdentityDbContext db) => _db = db;

    public async Task<Subscription?> FindByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await _db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId, ct);

    public async Task<Subscription?> FindByStripeCustomerIdAsync(string customerId, CancellationToken ct = default)
        => await _db.Subscriptions.FirstOrDefaultAsync(s => s.StripeCustomerId == customerId, ct);

    public async Task<IReadOnlyList<Subscription>> GetAllAsync(CancellationToken ct = default)
        => await _db.Subscriptions.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt).ToListAsync(ct);

    public async Task CreateAsync(Subscription subscription, CancellationToken ct = default)
    {
        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
