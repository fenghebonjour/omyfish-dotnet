using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Commands;

public sealed record ApplyPaymentEventCommand(PaymentEvent Event) : ICommand<bool>;

internal sealed class ApplyPaymentEventCommandHandler : ICommandHandler<ApplyPaymentEventCommand, bool>
{
    private readonly BillingService _billing;

    public ApplyPaymentEventCommandHandler(BillingService billing) => _billing = billing;

    public Task<bool> Handle(ApplyPaymentEventCommand command, CancellationToken ct) =>
        _billing.ApplyEventAsync(command.Event, ct);
}
