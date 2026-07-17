using MediatR;
using OMyFish.SpeciesService.Application.Queries;

namespace OMyFish.SpeciesService.Api.Endpoints;

public static class BiteScoreEndpoints
{
    public static void MapBiteScoreEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/species/bite-score");

        group.MapGet("/forecast", async (
            double lat,
            double lon,
            IMediator mediator,
            string species = "general",
            int hours = 336,
            CancellationToken ct = default) =>
        {
            try
            {
                var result = await mediator.Send(
                    new GetBiteForecastQuery(lat, lon, species, Math.Clamp(hours, 1, 336)), ct);
                return Results.Ok(result);
            }
            catch (HttpRequestException)
            {
                return Results.Problem(
                    title: "AI service unavailable",
                    detail: "The bite-score service is unreachable or its weather provider is down. Try again shortly.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .AllowAnonymous()
        .WithName("GetBiteForecast");

        group.MapGet("/today", async (
            double lat,
            double lon,
            IMediator mediator,
            string species = "general",
            CancellationToken ct = default) =>
        {
            try
            {
                var result = await mediator.Send(
                    new GetBiteForecastQuery(lat, lon, species, 24), ct);
                return Results.Ok(result);
            }
            catch (HttpRequestException)
            {
                return Results.Problem(
                    title: "AI service unavailable",
                    detail: "The bite-score service is unreachable or its weather provider is down. Try again shortly.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .AllowAnonymous()
        .WithName("GetBiteToday");
    }
}
