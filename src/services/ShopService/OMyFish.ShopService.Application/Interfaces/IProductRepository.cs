using OMyFish.ShopService.Domain.Entities;

namespace OMyFish.ShopService.Application.Interfaces;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetAllActiveAsync(CancellationToken ct = default);
    Task<Product?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Product product, CancellationToken ct = default);
}
