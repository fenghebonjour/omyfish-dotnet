using OMyFish.ShopService.Domain.ValueObjects;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Commands;

// GuestEmail is already resolved by the caller: required (and validated) for a guest
// checkout, null for an account checkout.
public sealed record CheckoutCommand(
    CartOwner Owner, string? GuestEmail, string SuccessUrl, string CancelUrl) : ICommand<CheckoutResultDto>;

public sealed record CheckoutResultDto(Guid OrderId, string CheckoutUrl);

public sealed record ConfirmPaymentWebhookCommand(
    string Payload, IReadOnlyDictionary<string, string> Headers) : ICommand<bool>;
