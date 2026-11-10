using OMyFish.ShopService.Application.Dtos;

namespace OMyFish.ShopService.Application.Interfaces;

/// Read cache for the product catalog. Implementations must never throw on a cache-backend
/// failure — a miss (null) just sends the caller to the database.
///
/// The cached StockQuantity is a display value, not an authoritative one: the actual admission
/// check (IProductRepository.TryDecreaseStockAsync) always hits Postgres and never reads this
/// cache, so overselling correctness is unaffected. Stock changes (checkout, cancel, return)
/// don't evict it, so a displayed count can lag by up to the L1 TTL — accepted, same tradeoff
/// as omyfish-java's ProductCache.
public interface IProductCatalogCache
{
    /// Negative cache for ids just looked up and not found — repeated lookups of a nonexistent
    /// id (cache penetration) don't each cost a database round-trip.
    bool IsKnownMissing(Guid id);
    void MarkMissing(Guid id);

    Task<ProductDto?> GetProductAsync(Guid id, CancellationToken ct = default);
    Task PutProductAsync(ProductDto product, CancellationToken ct = default);

    /// Also evicts the list (it embeds each product).
    Task EvictProductAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ProductDto>?> GetListAsync(CancellationToken ct = default);
    Task PutListAsync(IReadOnlyList<ProductDto> products, CancellationToken ct = default);
}
