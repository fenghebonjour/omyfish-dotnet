using OMyFish.ShopService.Domain.Entities;

namespace OMyFish.ShopService.Application.Interfaces;

/// Read-only catalog queries (browse/list, product detail), routed to the Postgres read replica
/// when one is configured. Writes — including the stock decrement — always go through
/// IProductRepository on the primary. Replica lag means a just-uploaded product image can show
/// up a moment late here; the catalog cache already accepts staleness on the same scale.
public interface IProductCatalogReader
{
    Task<IReadOnlyList<Product>> GetAllActiveAsync(CancellationToken ct = default);
    Task<Product?> FindByIdAsync(Guid id, CancellationToken ct = default);
}
