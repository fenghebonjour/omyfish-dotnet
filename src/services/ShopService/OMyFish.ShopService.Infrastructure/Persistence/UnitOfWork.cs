using OMyFish.ShopService.Application.Interfaces;

namespace OMyFish.ShopService.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly ShopDbContext _db;

    public UnitOfWork(ShopDbContext db) => _db = db;

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
