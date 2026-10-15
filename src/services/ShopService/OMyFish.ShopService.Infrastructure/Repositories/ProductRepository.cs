using Microsoft.EntityFrameworkCore;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Infrastructure.Persistence;

namespace OMyFish.ShopService.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly ShopDbContext _db;

    public ProductRepository(ShopDbContext db) => _db = db;

    public async Task<IReadOnlyList<Product>> GetAllActiveAsync(CancellationToken ct = default)
        => await _db.Products.AsNoTracking().Where(p => p.Active).ToListAsync(ct);

    public Task<Product?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task AddAsync(Product product, CancellationToken ct = default)
    {
        _db.Products.Add(product);
        return Task.CompletedTask;
    }
}
