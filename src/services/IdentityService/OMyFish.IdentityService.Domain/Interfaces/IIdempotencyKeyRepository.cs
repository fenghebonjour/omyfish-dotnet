using OMyFish.IdentityService.Domain.Entities;

namespace OMyFish.IdentityService.Domain.Interfaces;

public interface IIdempotencyKeyRepository
{
    Task<IdempotencyRecord?> FindAsync(string idempotencyKey, string endpoint, CancellationToken ct = default);

    /// Throws IdempotencyConflictException if a concurrent request already reserved this
    /// exact (key, endpoint) pair — the unique constraint on the table is the actual guard;
    /// this just translates the resulting DB exception into a typed one.
    Task<IdempotencyRecord> ReserveAsync(
        string idempotencyKey, string endpoint, Guid userId, CancellationToken ct = default);

    Task SaveAsync(IdempotencyRecord record, CancellationToken ct = default);
    Task DeleteAsync(IdempotencyRecord record, CancellationToken ct = default);
}

/// Thrown when an idempotency key is reused in a way the caller shouldn't rely on:
/// a concurrent reservation race, or a key presented by a different user than the one
/// who reserved it.
public sealed class IdempotencyConflictException(string message) : Exception(message);
