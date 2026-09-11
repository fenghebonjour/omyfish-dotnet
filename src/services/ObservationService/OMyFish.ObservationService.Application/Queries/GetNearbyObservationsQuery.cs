using OMyFish.ObservationService.Application.Interfaces;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ObservationService.Application.Queries;

public sealed record GetNearbyObservationsQuery(double Latitude, double Longitude, double RadiusKm)
    : IQuery<IReadOnlyList<NearbyObservationDto>>;

public sealed record NearbyObservationDto(
    Guid Id,
    string SpeciesName,
    double TopConfidence,
    double Latitude,
    double Longitude,
    DateTime ObservedAt,
    double DistanceKm);

internal sealed class GetNearbyObservationsQueryHandler
    : IQueryHandler<GetNearbyObservationsQuery, IReadOnlyList<NearbyObservationDto>>
{
    private readonly IObservationRepository _repo;
    public GetNearbyObservationsQueryHandler(IObservationRepository repo) => _repo = repo;

    public Task<IReadOnlyList<NearbyObservationDto>> Handle(GetNearbyObservationsQuery query, CancellationToken ct)
        => _repo.GetNearbyAsync(query.Latitude, query.Longitude, query.RadiusKm, ct);
}
