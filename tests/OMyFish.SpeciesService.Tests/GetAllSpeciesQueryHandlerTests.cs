using NSubstitute;
using OMyFish.SpeciesService.Application.Interfaces;
using OMyFish.SpeciesService.Application.Queries;
using OMyFish.SpeciesService.Domain.Entities;
using Xunit;

namespace OMyFish.SpeciesService.Tests;

public class GetAllSpeciesQueryHandlerTests
{
    private readonly ISpeciesRepository _repo = Substitute.For<ISpeciesRepository>();
    private readonly GetAllSpeciesQueryHandler _handler;

    public GetAllSpeciesQueryHandlerTests()
    {
        _handler = new GetAllSpeciesQueryHandler(_repo);
        _repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Species>
        {
            Species.Create("Sander vitreus", "Walleye", "Percidae", "LC", "Lake", "NA", "", true),
            Species.Create("Thunnus thynnus", "Bluefin Tuna", "Scombridae", "EN", "Ocean", "Atlantic", "", false),
        });
    }

    [Fact]
    public async Task Handle_WithoutFilter_ReturnsAll()
    {
        var result = await _handler.Handle(new GetAllSpeciesQuery(), CancellationToken.None);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Handle_FiltersByNorthAmericanFreshwater()
    {
        var freshwater = await _handler.Handle(new GetAllSpeciesQuery(true), CancellationToken.None);
        Assert.Equal("Sander vitreus", Assert.Single(freshwater).ScientificName);

        var other = await _handler.Handle(new GetAllSpeciesQuery(false), CancellationToken.None);
        Assert.Equal("Thunnus thynnus", Assert.Single(other).ScientificName);
    }
}
