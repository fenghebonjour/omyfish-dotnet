using OMyFish.ObservationService.Application.Interfaces;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ObservationService.Application.Queries;

public sealed record GetGeoJsonQuery : IQuery<object>;

internal sealed class GetGeoJsonQueryHandler : IQueryHandler<GetGeoJsonQuery, object>
{
    private readonly IObservationRepository _repo;
    public GetGeoJsonQueryHandler(IObservationRepository repo) => _repo = repo;

    public async Task<object> Handle(GetGeoJsonQuery query, CancellationToken ct)
    {
        var observations = await _repo.GetWithLocationAsync(ct);

        var features = observations.Select(o => new
        {
            type = "Feature",
            geometry = new
            {
                type = "Point",
                coordinates = new[] { o.Longitude!.Value, o.Latitude!.Value }
            },
            properties = new
            {
                id = o.Id,
                speciesName = o.SpeciesName,
                scientificName = o.ScientificName,
                confidence = o.TopConfidence,
                notes = o.Notes,
                observedAt = o.ObservedAt
            }
        });

        return new
        {
            type = "FeatureCollection",
            features
        };
    }
}
