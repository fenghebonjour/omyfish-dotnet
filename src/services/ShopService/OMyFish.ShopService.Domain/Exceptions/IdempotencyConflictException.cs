namespace OMyFish.ShopService.Domain.Exceptions;

/// A checkout with this Idempotency-Key is already in flight (a concurrent reservation race on
/// the unique constraint, or a retry that arrived before the first attempt finished). ->409.
public sealed class IdempotencyConflictException(string message) : Exception(message);
