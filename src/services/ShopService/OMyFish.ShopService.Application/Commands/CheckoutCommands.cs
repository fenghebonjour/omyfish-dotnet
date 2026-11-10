using OMyFish.ShopService.Domain.ValueObjects;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Commands;

// GuestEmail is already resolved by the caller: required (and validated) for a guest
// checkout, null for an account checkout.
public sealed record CheckoutCommand(
    CartOwner Owner, string? GuestEmail, string SuccessUrl, string CancelUrl, string IdempotencyKey)
    : ICommand<CheckoutResultDto>;

// Replayed: this is a retry's replay of an earlier attempt with the same Idempotency-Key, not a
// new order (only surfaced as a metric label, not in the HTTP response).
public sealed record CheckoutResultDto(Guid OrderId, string CheckoutUrl, bool Replayed = false);

public sealed record ConfirmPaymentWebhookCommand(
    string Processor, string Payload, IReadOnlyDictionary<string, string> Headers) : ICommand<bool>;
