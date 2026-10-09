using Microsoft.EntityFrameworkCore;
using Npgsql;
using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Domain.Interfaces;
using OMyFish.IdentityService.Infrastructure.Persistence;

namespace OMyFish.IdentityService.Infrastructure.Repositories;

public class IdempotencyKeyRepository : IIdempotencyKeyRepository
{
    private const string UniqueViolation = "23505";

    private readonly IdentityDbContext _db;

    public IdempotencyKeyRepository(IdentityDbContext db) => _db = db;

    public async Task<IdempotencyRecord?> FindAsync(
        string idempotencyKey, string endpoint, CancellationToken ct = default)
        => await _db.IdempotencyRecords.FirstOrDefaultAsync(
            r => r.IdempotencyKey == idempotencyKey && r.Endpoint == endpoint, ct);

    public async Task<IdempotencyRecord> ReserveAsync(
        string idempotencyKey, string endpoint, Guid userId, CancellationToken ct = default)
    {
        var record = IdempotencyRecord.Reserve(idempotencyKey, endpoint, userId);
        _db.IdempotencyRecords.Add(record);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.Entry(record).State = EntityState.Detached;
            throw new IdempotencyConflictException(
                "A request with this idempotency key is already in progress");
        }
        return record;
    }

    public Task SaveAsync(IdempotencyRecord record, CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public Task DeleteAsync(IdempotencyRecord record, CancellationToken ct = default)
    {
        _db.IdempotencyRecords.Remove(record);
        return _db.SaveChangesAsync(ct);
    }
}
