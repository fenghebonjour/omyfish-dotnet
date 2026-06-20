using OMyFish.ObservationService.Application.Interfaces;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ObservationService.Application.Commands;

public sealed record DeleteObservationCommand(Guid ObservationId, Guid UserId) : ICommand<bool>;

internal sealed class DeleteObservationCommandHandler : ICommandHandler<DeleteObservationCommand, bool>
{
    private readonly IObservationRepository _repo;
    public DeleteObservationCommandHandler(IObservationRepository repo) => _repo = repo;

    public Task<bool> Handle(DeleteObservationCommand command, CancellationToken ct)
        => _repo.DeleteAsync(command.ObservationId, command.UserId, ct);
}
