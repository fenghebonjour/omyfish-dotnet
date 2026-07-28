using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OMyFish.ObservationService.Application.Commands;
using OMyFish.ObservationService.Application.Queries;

namespace OMyFish.ObservationService.Api.Endpoints;

public static class ObservationEndpoints
{
    public static void MapObservationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/observations").RequireAuthorization();

        group.MapPost("/", async (
            CreateObservationRequest req,
            IMediator mediator,
            HttpContext ctx,
            CancellationToken ct = default) =>
        {
            var userId = GetUserId(ctx);
            if (userId == Guid.Empty) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.ImageStorageKey))
                return Results.BadRequest("imageStorageKey is required.");

            var command = new CreateObservationCommand(
                userId, req.SpeciesName, req.ScientificName, req.TopConfidence,
                req.ImageStorageKey, req.Latitude, req.Longitude, req.Notes);

            var result = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/observations/{result.ObservationId}", result);
        })
        .WithName("CreateObservation");

        group.MapGet("/", async (
            IMediator mediator,
            HttpContext ctx,
            bool myOnly = false,
            CancellationToken ct = default) =>
        {
            var userId = myOnly ? GetUserId(ctx) : (Guid?)null;
            var result = await mediator.Send(new GetObservationsQuery(userId), ct);
            return Results.Ok(result);
        })
        .WithName("GetObservations");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(new GetObservationByIdQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetObservationById");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            HttpContext ctx,
            CancellationToken ct = default) =>
        {
            var userId = GetUserId(ctx);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var deleted = await mediator.Send(new DeleteObservationCommand(id, userId), ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteObservation");

        // Public GeoJSON endpoint for map display
        app.MapGet("/api/v1/observations/geojson", async (
            IMediator mediator,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(new GetGeoJsonQuery(), ct);
            return Results.Ok(result);
        })
        .WithName("GetObservationsGeoJson");
    }

    private static Guid GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst("sub")?.Value
               ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

// JSON body — the image is already stored (species-service's /identify
// uploaded it and returned this key), so observation-create just references
// it instead of re-uploading, matching the family's two-step contract.
public sealed record CreateObservationRequest(
    string SpeciesName,
    string? ScientificName,
    double TopConfidence,
    string ImageStorageKey,
    double? Latitude,
    double? Longitude,
    string? Notes);
