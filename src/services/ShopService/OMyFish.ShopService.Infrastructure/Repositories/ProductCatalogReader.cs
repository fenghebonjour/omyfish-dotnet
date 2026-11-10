using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Infrastructure.Persistence;

namespace OMyFish.ShopService.Infrastructure.Repositories;

/// Catalog reads against the read replica (ShopReadDbContext), falling back to the primary if
/// the replica is unreachable — a replica outage only costs the cache-miss overflow that was
/// already going to the database, never availability. With no replica configured,
/// ShopReadDbContext just points at the primary.
public sealed class ProductCatalogReader(
    ShopReadDbContext replica, ShopDbContext primary, ILogger<ProductCatalogReader> logger) : IProductCatalogReader
{
    public Task<IReadOnlyList<Product>> GetAllActiveAsync(CancellationToken ct = default) =>
        WithFallbackAsync<IReadOnlyList<Product>>(
            async db => await db.Products.AsNoTracking().Where(p => p.Active).ToListAsync(ct));

    public Task<Product?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        WithFallbackAsync(db => db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct));

    private async Task<T> WithFallbackAsync<T>(Func<ShopDbContext, Task<T>> query)
    {
        try
        {
            return await query(replica);
        }
        // EF Core surfaces a connection failure it classifies as transient as an
        // InvalidOperationException wrapping the NpgsqlException, not the NpgsqlException itself.
        catch (Exception e) when (e is NpgsqlException or TimeoutException
                                      or InvalidOperationException { InnerException: NpgsqlException })
        {
            logger.LogWarning(e, "Read replica unavailable — serving catalog read from the primary");
            return await query(primary);
        }
    }
}
