using OMyFish.ShopService.Domain.Entities;

namespace OMyFish.ShopService.Application.Interfaces;

public interface IProductRepository
{
    Task<Product?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Product product, CancellationToken ct = default);

    /// One atomic conditional UPDATE (stock_quantity = stock_quantity - qty WHERE
    /// stock_quantity >= qty), executed immediately, not staged for SaveChangesAsync. False if
    /// there wasn't enough stock. Replaces load-decrement-save, which lost updates under
    /// concurrency and oversold.
    Task<bool> TryDecreaseStockAsync(Guid productId, int quantity, CancellationToken ct = default);

    /// Atomic counterpart for restocks (cancel, return), executed immediately. A tracked
    /// read-modify-write here would overwrite a concurrent TryDecreaseStockAsync with a stale
    /// value. False if the product doesn't exist.
    Task<bool> IncreaseStockAsync(Guid productId, int quantity, CancellationToken ct = default);
}
