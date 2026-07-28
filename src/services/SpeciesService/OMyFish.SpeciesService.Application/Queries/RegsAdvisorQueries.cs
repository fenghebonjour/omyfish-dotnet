using OMyFish.Shared.BuildingBlocks.CQRS;
using OMyFish.SpeciesService.Application.DTOs;

namespace OMyFish.SpeciesService.Application.Queries;

// Thin proxy queries to omyfish-ai's /regs/* endpoints (Quebec fishing regs
// advisor) — same pattern as GetBiteForecastQuery, chatbot/retrieval logic
// stays in omyfish-ai.

public sealed record GetRegsLimitsQuery(
    double Lat, double Lon, string Species) : IQuery<RegsLimitsDto>;

public sealed record GetRegsZonesGeoJsonQuery : IQuery<IReadOnlyDictionary<string, object>>;

public sealed record GetRegsConsumptionStationsQuery(
    double Lat, double Lon, int Limit) : IQuery<IReadOnlyList<RegsStationDto>>;

public sealed record GetRegsConsumptionQuery(
    double Lat, double Lon, string Species, double? SizeCm) : IQuery<RegsConsumptionDto>;

public sealed record AskRegsQuery(string Question) : IQuery<RegsAnswerDto>;
