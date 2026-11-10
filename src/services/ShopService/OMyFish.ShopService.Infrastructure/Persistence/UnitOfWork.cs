using OMyFish.ShopService.Application.Interfaces;

namespace OMyFish.ShopService.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly ShopDbContext _db;

    public UnitOfWork(ShopDbContext db) => _db = db;

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            // Rolled back on dispose. Drop whatever the failed work left tracked, so a later
            // SaveChangesAsync on this same scoped context (e.g. deleting the idempotency
            // reservation) doesn't try to flush it again.
            _db.ChangeTracker.Clear();
            throw;
        }
    }
}
