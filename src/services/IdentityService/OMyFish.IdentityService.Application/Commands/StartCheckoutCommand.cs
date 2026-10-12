using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Commands;

public sealed record StartCheckoutCommand(Guid UserId, string Plan, string IdempotencyKey)
    : ICommand<SubscriptionIntentResult?>;

internal sealed class StartCheckoutCommandHandler : ICommandHandler<StartCheckoutCommand, SubscriptionIntentResult?>
{
    private readonly BillingService _billing;

    public StartCheckoutCommandHandler(BillingService billing) => _billing = billing;

    public Task<SubscriptionIntentResult?> Handle(StartCheckoutCommand command, CancellationToken ct) =>
        _billing.StartCheckoutAsync(command.UserId, command.Plan, command.IdempotencyKey, ct);
}
