using OMyFish.IdentityService.Application.Queries;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Commands;

public sealed record RevokeSubscriptionCommand(Guid UserId) : ICommand<SubscriptionDto?>;

internal sealed class RevokeSubscriptionCommandHandler : ICommandHandler<RevokeSubscriptionCommand, SubscriptionDto?>
{
    private readonly BillingService _billing;

    public RevokeSubscriptionCommandHandler(BillingService billing) => _billing = billing;

    public async Task<SubscriptionDto?> Handle(RevokeSubscriptionCommand command, CancellationToken ct)
    {
        var sub = await _billing.RevokeAsync(command.UserId, ct);
        return sub is null ? null
            : new SubscriptionDto(sub.EffectiveStatus, sub.Plan, sub.TrialEnd, sub.CurrentPeriodEnd, sub.PaymentProcessor);
    }
}
