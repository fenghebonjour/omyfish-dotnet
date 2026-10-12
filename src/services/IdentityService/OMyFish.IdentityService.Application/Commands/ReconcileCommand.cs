using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Commands;

public sealed record ReconcileCommand(DateTime Since) : ICommand<ReconciliationResult>;

internal sealed class ReconcileCommandHandler : ICommandHandler<ReconcileCommand, ReconciliationResult>
{
    private readonly ReconciliationService _reconciliation;

    public ReconcileCommandHandler(ReconciliationService reconciliation) => _reconciliation = reconciliation;

    public Task<ReconciliationResult> Handle(ReconcileCommand command, CancellationToken ct) =>
        _reconciliation.ReconcileAsync(command.Since, ct);
}
