using OMyFish.IdentityService.Application.Queries;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Commands;

public sealed record ExtendTrialCommand(Guid UserId, int Days) : ICommand<SubscriptionDto>;

internal sealed class ExtendTrialCommandHandler : ICommandHandler<ExtendTrialCommand, SubscriptionDto>
{
    private readonly BillingService _billing;

    public ExtendTrialCommandHandler(BillingService billing) => _billing = billing;

    public async Task<SubscriptionDto> Handle(ExtendTrialCommand command, CancellationToken ct)
    {
        var sub = await _billing.ExtendTrialAsync(command.UserId, command.Days, ct);
        return new SubscriptionDto(sub.EffectiveStatus, sub.Plan, sub.TrialEnd, sub.CurrentPeriodEnd, sub.PaymentProcessor);
    }
}
