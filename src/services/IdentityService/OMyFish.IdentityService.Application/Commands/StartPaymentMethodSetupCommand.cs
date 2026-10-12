using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Commands;

public sealed record StartPaymentMethodSetupCommand(Guid UserId) : ICommand<SetupIntentResult?>;

internal sealed class StartPaymentMethodSetupCommandHandler
    : ICommandHandler<StartPaymentMethodSetupCommand, SetupIntentResult?>
{
    private readonly BillingService _billing;

    public StartPaymentMethodSetupCommandHandler(BillingService billing) => _billing = billing;

    public Task<SetupIntentResult?> Handle(StartPaymentMethodSetupCommand command, CancellationToken ct) =>
        _billing.StartPaymentMethodSetupAsync(command.UserId, ct);
}
