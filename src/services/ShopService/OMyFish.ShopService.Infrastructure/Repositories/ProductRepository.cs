using Microsoft.EntityFrameworkCore;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Infrastructure.Persistence;

namespace OMyFish.ShopService.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly ShopDbContext _db;

    public ProductRepository(ShopDbContext db) => _db = db;

    public Task<Product?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task AddAsync(Product product, CancellationToken ct = default)
    {
        _db.Products.Add(product);
        return Task.CompletedTask;
    }

    public async Task<bool> TryDecreaseStockAsync(Guid productId, int quantity, CancellationToken ct = default)
        => await _db.Products
            .Where(p => p.Id == productId && p.StockQuantity >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity), ct) == 1;

    public async Task<bool> IncreaseStockAsync(Guid productId, int quantity, CancellationToken ct = default)
        => await _db.Products
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity + quantity), ct) == 1;
}
