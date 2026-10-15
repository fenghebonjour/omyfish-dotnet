using OMyFish.ShopService.Domain.Entities;

namespace OMyFish.ShopService.Application.Interfaces;

public interface ICartRepository
{
    Task<Cart?> FindByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<Cart?> FindByGuestTokenAsync(Guid guestToken, CancellationToken ct = default);
    Task AddAsync(Cart cart, CancellationToken ct = default);
    Task DeleteByGuestTokenAsync(Guid guestToken, CancellationToken ct = default);
}
