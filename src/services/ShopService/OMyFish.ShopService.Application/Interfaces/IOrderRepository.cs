using OMyFish.ShopService.Domain.Entities;

namespace OMyFish.ShopService.Application.Interfaces;

public interface IOrderRepository
{
    Task<Order?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<Order?> FindByStripeSessionIdAsync(string stripeSessionId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Order order, CancellationToken ct = default);

    // PendingPayment orders created before cutoff — abandoned checkouts a processor never
    // reported back on (PayPal/Adyen have no expiry webhook at all), for StaleOrderSweepService.
    Task<IReadOnlyList<Guid>> GetPendingPaymentIdsOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
