using OMyFish.Shared.BuildingBlocks.CQRS;
using OMyFish.SpeciesService.Application.DTOs;
using OMyFish.SpeciesService.Application.Interfaces;

namespace OMyFish.SpeciesService.Application.Queries;

internal sealed class GetBiteForecastQueryHandler : IQueryHandler<GetBiteForecastQuery, BiteForecastDto>
{
    private readonly IAIServiceClient _ai;
    public GetBiteForecastQueryHandler(IAIServiceClient ai) => _ai = ai;

    public Task<BiteForecastDto> Handle(GetBiteForecastQuery query, CancellationToken ct)
        => _ai.GetBiteForecastAsync(query.Lat, query.Lon, query.Species, query.Hours, ct);
}
