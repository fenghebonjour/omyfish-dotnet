using OMyFish.Shared.BuildingBlocks.CQRS;
using OMyFish.SpeciesService.Application.DTOs;
using OMyFish.SpeciesService.Application.Interfaces;

namespace OMyFish.SpeciesService.Application.Queries;

internal sealed class GetRegsLimitsQueryHandler : IQueryHandler<GetRegsLimitsQuery, RegsLimitsDto>
{
    private readonly IAIServiceClient _ai;
    public GetRegsLimitsQueryHandler(IAIServiceClient ai) => _ai = ai;

    public Task<RegsLimitsDto> Handle(GetRegsLimitsQuery query, CancellationToken ct)
        => _ai.GetRegsLimitsAsync(query.Lat, query.Lon, query.Species, ct);
}

internal sealed class GetRegsZonesGeoJsonQueryHandler
    : IQueryHandler<GetRegsZonesGeoJsonQuery, IReadOnlyDictionary<string, object>>
{
    private readonly IAIServiceClient _ai;
    public GetRegsZonesGeoJsonQueryHandler(IAIServiceClient ai) => _ai = ai;

    public Task<IReadOnlyDictionary<string, object>> Handle(GetRegsZonesGeoJsonQuery query, CancellationToken ct)
        => _ai.GetRegsZonesGeoJsonAsync(ct);
}

internal sealed class GetRegsConsumptionStationsQueryHandler
    : IQueryHandler<GetRegsConsumptionStationsQuery, IReadOnlyList<RegsStationDto>>
{
    private readonly IAIServiceClient _ai;
    public GetRegsConsumptionStationsQueryHandler(IAIServiceClient ai) => _ai = ai;

    public Task<IReadOnlyList<RegsStationDto>> Handle(GetRegsConsumptionStationsQuery query, CancellationToken ct)
        => _ai.GetRegsConsumptionStationsAsync(query.Lat, query.Lon, query.Limit, ct);
}

internal sealed class GetRegsConsumptionQueryHandler : IQueryHandler<GetRegsConsumptionQuery, RegsConsumptionDto>
{
    private readonly IAIServiceClient _ai;
    public GetRegsConsumptionQueryHandler(IAIServiceClient ai) => _ai = ai;

    public Task<RegsConsumptionDto> Handle(GetRegsConsumptionQuery query, CancellationToken ct)
        => _ai.GetRegsConsumptionAsync(query.Lat, query.Lon, query.Species, query.SizeCm, ct);
}

internal sealed class AskRegsQueryHandler : IQueryHandler<AskRegsQuery, RegsAnswerDto>
{
    private readonly IAIServiceClient _ai;
    public AskRegsQueryHandler(IAIServiceClient ai) => _ai = ai;

    public Task<RegsAnswerDto> Handle(AskRegsQuery query, CancellationToken ct)
        => _ai.AskRegsAsync(query.Question, ct);
}
