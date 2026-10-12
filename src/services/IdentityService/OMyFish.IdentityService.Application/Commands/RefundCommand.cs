using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Commands;

public sealed record RefundCommand(Guid UserId, long? AmountCents, string IdempotencyKey) : ICommand<RefundResult?>;

internal sealed class RefundCommandHandler : ICommandHandler<RefundCommand, RefundResult?>
{
    private readonly BillingService _billing;

    public RefundCommandHandler(BillingService billing) => _billing = billing;

    public Task<RefundResult?> Handle(RefundCommand command, CancellationToken ct) =>
        _billing.RefundAsync(command.UserId, command.AmountCents, command.IdempotencyKey, ct);
}
