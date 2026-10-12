using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Commands;

public sealed record CreatePortalSessionCommand(Guid UserId, string ReturnUrl) : ICommand<string?>;

internal sealed class CreatePortalSessionCommandHandler : ICommandHandler<CreatePortalSessionCommand, string?>
{
    private readonly BillingService _billing;

    public CreatePortalSessionCommandHandler(BillingService billing) => _billing = billing;

    public Task<string?> Handle(CreatePortalSessionCommand command, CancellationToken ct) =>
        _billing.CreatePortalSessionAsync(command.UserId, command.ReturnUrl, ct);
}
