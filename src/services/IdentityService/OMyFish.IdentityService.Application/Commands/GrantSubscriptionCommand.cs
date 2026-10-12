using OMyFish.IdentityService.Application.Queries;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Commands;

public sealed record GrantSubscriptionCommand(Guid UserId, string Plan, int Days) : ICommand<SubscriptionDto>;

internal sealed class GrantSubscriptionCommandHandler : ICommandHandler<GrantSubscriptionCommand, SubscriptionDto>
{
    private readonly BillingService _billing;

    public GrantSubscriptionCommandHandler(BillingService billing) => _billing = billing;

    public async Task<SubscriptionDto> Handle(GrantSubscriptionCommand command, CancellationToken ct)
    {
        var sub = await _billing.GrantAsync(command.UserId, command.Plan, command.Days, ct);
        return new SubscriptionDto(sub.EffectiveStatus, sub.Plan, sub.TrialEnd, sub.CurrentPeriodEnd, sub.PaymentProcessor);
    }
}
