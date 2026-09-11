using OMyFish.ObservationService.Application.Interfaces;
using OMyFish.ObservationService.Domain.Entities;
using OMyFish.ObservationService.Domain.ValueObjects;
using OMyFish.Shared.BuildingBlocks.CQRS;
using OMyFish.Shared.BuildingBlocks.Messaging;

namespace OMyFish.ObservationService.Application.Commands;

internal sealed class CreateObservationCommandHandler
    : ICommandHandler<CreateObservationCommand, CreateObservationResult>
{
    private readonly IObservationRepository _repo;
    private readonly IMessagePublisher _publisher;
    private readonly IUnitOfWork _unitOfWork;

    public CreateObservationCommandHandler(
        IObservationRepository repo,
        IMessagePublisher publisher,
        IUnitOfWork unitOfWork)
    {
        _repo = repo;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
    }

    public async Task<CreateObservationResult> Handle(
        CreateObservationCommand command, CancellationToken ct)
    {
        // The image is already stored — identify persisted it and returned this
        // key; we just reference it here rather than re-uploading.
        GpsCoordinates? location = null;
        if (command.Latitude.HasValue && command.Longitude.HasValue)
            location = GpsCoordinates.Create(command.Latitude.Value, command.Longitude.Value);

        var obs = Observation.Create(
            command.UserId,
            command.SpeciesName,
            command.ScientificName,
            command.TopConfidence,
            command.ImageStorageKey,
            location,
            null,
            command.Notes);

        await _repo.AddAsync(obs, ct);

        foreach (var evt in obs.PullDomainEvents())
            await _publisher.PublishAsync(evt, ct);

        // Commits the observation row and the outbox message together — a crash between
        // "save" and "publish" can no longer drop the event (BACKLOG.md item F §2.3).
        await _unitOfWork.SaveChangesAsync(ct);

        return new CreateObservationResult(
            obs.Id, obs.SpeciesName, command.ImageStorageKey, obs.ObservedAt);
    }
}
