using OMyFish.Shared.BuildingBlocks.CQRS;
using OMyFish.SpeciesService.Application.DTOs;

namespace OMyFish.SpeciesService.Application.Queries;

// Species accepts a profile key ("largemouth_bass") or any resolvable
// common/scientific name (e.g. from a confirmed fish ID) — the AI client
// resolves it, falling back to the "general" profile.
public sealed record GetBiteForecastQuery(
    double Lat, double Lon, string Species, int Hours) : IQuery<BiteForecastDto>;
