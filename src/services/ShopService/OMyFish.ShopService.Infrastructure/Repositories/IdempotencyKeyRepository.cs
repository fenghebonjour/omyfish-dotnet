using Microsoft.EntityFrameworkCore;
using Npgsql;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.ShopService.Infrastructure.Persistence;

namespace OMyFish.ShopService.Infrastructure.Repositories;

public class IdempotencyKeyRepository : IIdempotencyKeyRepository
{
    private const string UniqueViolation = "23505";

    private readonly ShopDbContext _db;

    public IdempotencyKeyRepository(ShopDbContext db) => _db = db;

    public Task<IdempotencyRecord?> FindAsync(string idempotencyKey, string endpoint, CancellationToken ct = default)
        => _db.IdempotencyRecords.FirstOrDefaultAsync(
            r => r.IdempotencyKey == idempotencyKey && r.Endpoint == endpoint, ct);

    public async Task<IdempotencyRecord> ReserveAsync(
        string idempotencyKey, string endpoint, string ownerKey, CancellationToken ct = default)
    {
        var record = IdempotencyRecord.Reserve(idempotencyKey, endpoint, ownerKey);
        _db.IdempotencyRecords.Add(record);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.Entry(record).State = EntityState.Detached;
            throw new IdempotencyConflictException("A checkout with this idempotency key is already in progress");
        }
        return record;
    }

    public Task SaveAsync(IdempotencyRecord record, CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    // Immediate DELETE rather than Remove + SaveChangesAsync: this runs on the failure path,
    // where the shared context may still hold the change that just failed — SaveChangesAsync
    // would re-flush it, throw again, and leave the key stuck "in progress".
    public Task DeleteAsync(IdempotencyRecord record, CancellationToken ct = default)
        => _db.IdempotencyRecords.Where(r => r.Id == record.Id).ExecuteDeleteAsync(ct);
}
