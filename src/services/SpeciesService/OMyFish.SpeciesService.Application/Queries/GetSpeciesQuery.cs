using OMyFish.Shared.BuildingBlocks.CQRS;
using OMyFish.SpeciesService.Application.DTOs;

namespace OMyFish.SpeciesService.Application.Queries;

public sealed record GetSpeciesQuery(string ScientificName) : IQuery<SpeciesDto?>;

public sealed record GetAllSpeciesQuery(bool? NorthAmericanFreshwater = null)
    : IQuery<IReadOnlyList<SpeciesDto>>;
