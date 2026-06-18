using OMyFish.ObservationService.Application.Interfaces;
using OMyFish.ObservationService.Domain.Entities;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ObservationService.Application.Queries.Handlers;

internal sealed class GetObservationsQueryHandler
    : IQueryHandler<GetObservationsQuery, IReadOnlyList<ObservationDto>>
{
    private readonly IObservationRepository _repo;
    private readonly IStorageService _storage;

    public GetObservationsQueryHandler(IObservationRepository repo, IStorageService storage)
    {
        _repo = repo;
        _storage = storage;
    }

    public async Task<IReadOnlyList<ObservationDto>> Handle(GetObservationsQuery query, CancellationToken ct)
    {
        var observations = query.UserId.HasValue
            ? await _repo.GetByUserIdAsync(query.UserId.Value, ct)
            : await _repo.GetAllAsync(ct);

        return observations.Select(o => ToDto(o, _storage)).ToList();
    }

    internal static ObservationDto ToDto(Observation o, IStorageService storage) => new(
        o.Id, o.UserId, o.SpeciesName, o.ScientificName, o.TopConfidence,
        o.ImageStorageKey, storage.GetPublicUrl(o.ImageStorageKey),
        o.Location?.Latitude, o.Location?.Longitude,
        o.Notes, o.ObservedAt, o.CreatedAt);
}

internal sealed class GetObservationByIdQueryHandler
    : IQueryHandler<GetObservationByIdQuery, ObservationDto?>
{
    private readonly IObservationRepository _repo;
    private readonly IStorageService _storage;

    public GetObservationByIdQueryHandler(IObservationRepository repo, IStorageService storage)
    {
        _repo = repo;
        _storage = storage;
    }

    public async Task<ObservationDto?> Handle(GetObservationByIdQuery query, CancellationToken ct)
    {
        var o = await _repo.FindByIdAsync(query.Id, ct);
        return o is null ? null : GetObservationsQueryHandler.ToDto(o, _storage);
    }
}
