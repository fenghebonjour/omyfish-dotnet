using OMyFish.ShopService.Domain.Entities;

namespace OMyFish.ShopService.Application.Interfaces;

public interface IOrderRepository
{
    Task<Order?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<Order?> FindByStripeSessionIdAsync(string stripeSessionId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Order order, CancellationToken ct = default);
}
