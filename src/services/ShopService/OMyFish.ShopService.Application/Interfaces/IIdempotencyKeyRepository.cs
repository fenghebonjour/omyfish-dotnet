using OMyFish.ShopService.Domain.Entities;

namespace OMyFish.ShopService.Application.Interfaces;

public interface IIdempotencyKeyRepository
{
    Task<IdempotencyRecord?> FindAsync(string idempotencyKey, string endpoint, CancellationToken ct = default);

    /// Throws IdempotencyConflictException if a concurrent request already reserved this exact
    /// (key, endpoint) pair — the table's unique constraint is the actual guard; this just
    /// translates the resulting DB exception into a typed one.
    Task<IdempotencyRecord> ReserveAsync(
        string idempotencyKey, string endpoint, string ownerKey, CancellationToken ct = default);

    Task SaveAsync(IdempotencyRecord record, CancellationToken ct = default);
    Task DeleteAsync(IdempotencyRecord record, CancellationToken ct = default);
}
