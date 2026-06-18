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
    private readonly IStorageService _storage;
    private readonly IMessagePublisher _publisher;

    public CreateObservationCommandHandler(
        IObservationRepository repo,
        IStorageService storage,
        IMessagePublisher publisher)
    {
        _repo = repo;
        _storage = storage;
        _publisher = publisher;
    }

    public async Task<CreateObservationResult> Handle(
        CreateObservationCommand command, CancellationToken ct)
    {
        var storageKey = await _storage.UploadAsync(
            command.ImageStream,
            command.ImageFileName,
            command.ImageContentType,
            ct);

        GpsCoordinates? location = null;
        if (command.Latitude.HasValue && command.Longitude.HasValue)
            location = GpsCoordinates.Create(command.Latitude.Value, command.Longitude.Value);

        var obs = Observation.Create(
            command.UserId,
            command.SpeciesName,
            command.ScientificName,
            command.TopConfidence,
            storageKey,
            location,
            null,
            command.Notes);

        await _repo.AddAsync(obs, ct);

        foreach (var evt in obs.PullDomainEvents())
            await _publisher.PublishAsync(evt, ct);

        return new CreateObservationResult(
            obs.Id, obs.SpeciesName, storageKey, obs.ObservedAt);
    }
}
