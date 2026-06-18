using MediatR;
using OMyFish.SpeciesService.Application.Queries;

namespace OMyFish.SpeciesService.Api.Endpoints;

public static class SpeciesEndpoints
{
    public static void MapSpeciesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/species");

        group.MapGet("/", async (
            IMediator mediator,
            bool? northAmericanFreshwater = null,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(
                new GetAllSpeciesQuery(northAmericanFreshwater), ct);
            return Results.Ok(result);
        })
        .WithName("GetAllSpecies");

        group.MapGet("/{scientificName}", async (
            string scientificName,
            IMediator mediator,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(new GetSpeciesQuery(scientificName), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetSpecies");
    }
}
