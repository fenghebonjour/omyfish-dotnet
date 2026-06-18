using OMyFish.Shared.BuildingBlocks.CQRS;
using OMyFish.SpeciesService.Application.DTOs;
using OMyFish.SpeciesService.Application.Interfaces;
using OMyFish.SpeciesService.Domain.Entities;

namespace OMyFish.SpeciesService.Application.Queries;

internal sealed class GetSpeciesQueryHandler : IQueryHandler<GetSpeciesQuery, SpeciesDto?>
{
    private readonly ISpeciesRepository _repo;
    public GetSpeciesQueryHandler(ISpeciesRepository repo) => _repo = repo;

    public async Task<SpeciesDto?> Handle(GetSpeciesQuery query, CancellationToken ct)
    {
        var s = await _repo.FindByScientificNameAsync(query.ScientificName, ct);
        return s is null ? null : ToDto(s);
    }

    internal static SpeciesDto ToDto(Species s) => new(
        s.Id, s.ScientificName, s.CommonName, s.Family,
        s.ConservationStatus, s.Habitat, s.GeographicRange,
        s.Description, s.IsNorthAmericanFreshwater, s.ImageUrl);
}

internal sealed class GetAllSpeciesQueryHandler
    : IQueryHandler<GetAllSpeciesQuery, IReadOnlyList<SpeciesDto>>
{
    private readonly ISpeciesRepository _repo;
    public GetAllSpeciesQueryHandler(ISpeciesRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<SpeciesDto>> Handle(GetAllSpeciesQuery query, CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(ct);
        var filtered = query.NorthAmericanFreshwater.HasValue
            ? all.Where(s => s.IsNorthAmericanFreshwater == query.NorthAmericanFreshwater.Value)
            : all;
        return filtered.Select(GetSpeciesQueryHandler.ToDto).ToList();
    }
}
