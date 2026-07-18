using OMyFish.IdentityService.Domain.Entities;

namespace OMyFish.IdentityService.Domain.Interfaces;

public interface ISubscriptionRepository
{
    Task<Subscription?> FindByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<Subscription?> FindByStripeCustomerIdAsync(string customerId, CancellationToken ct = default);
    Task<IReadOnlyList<Subscription>> GetAllAsync(CancellationToken ct = default);
    Task CreateAsync(Subscription subscription, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
