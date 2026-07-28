using MediatR;
using OMyFish.SpeciesService.Application.Queries;

namespace OMyFish.SpeciesService.Api.Endpoints;

public static class RegsEndpoints
{
    public static void MapRegsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/species/regs").AllowAnonymous();

        group.MapGet("/limits", async (
            double lat, double lon, IMediator mediator,
            string species = "general", CancellationToken ct = default) =>
        {
            try
            {
                return Results.Ok(await mediator.Send(new GetRegsLimitsQuery(lat, lon, species), ct));
            }
            catch (HttpRequestException)
            {
                return RegsUnavailable();
            }
        }).WithName("GetRegsLimits");

        group.MapGet("/zones/geojson", async (IMediator mediator, CancellationToken ct = default) =>
        {
            try
            {
                return Results.Ok(await mediator.Send(new GetRegsZonesGeoJsonQuery(), ct));
            }
            catch (HttpRequestException)
            {
                return RegsUnavailable();
            }
        }).WithName("GetRegsZonesGeoJson");

        group.MapGet("/consumption/stations", async (
            double lat, double lon, IMediator mediator,
            int limit = 5, CancellationToken ct = default) =>
        {
            try
            {
                return Results.Ok(await mediator.Send(new GetRegsConsumptionStationsQuery(lat, lon, limit), ct));
            }
            catch (HttpRequestException)
            {
                return RegsUnavailable();
            }
        }).WithName("GetRegsConsumptionStations");

        group.MapGet("/consumption", async (
            double lat, double lon, IMediator mediator,
            string species = "general", double? sizeCm = null, CancellationToken ct = default) =>
        {
            try
            {
                return Results.Ok(await mediator.Send(new GetRegsConsumptionQuery(lat, lon, species, sizeCm), ct));
            }
            catch (HttpRequestException)
            {
                return RegsUnavailable();
            }
        }).WithName("GetRegsConsumption");

        group.MapPost("/ask", async (AskRequest req, IMediator mediator, CancellationToken ct = default) =>
        {
            try
            {
                return Results.Ok(await mediator.Send(new AskRegsQuery(req.Question), ct));
            }
            catch (HttpRequestException)
            {
                return RegsUnavailable();
            }
        }).WithName("AskRegs");
    }

    private static IResult RegsUnavailable() => Results.Problem(
        title: "AI service unavailable",
        detail: "The regs advisor is unreachable or its data provider is down. Try again shortly.",
        statusCode: StatusCodes.Status503ServiceUnavailable);

    public sealed record AskRequest(string Question);
}
