using MediatR;
using OMyFish.SpeciesService.Application.Commands;

namespace OMyFish.SpeciesService.Api.Endpoints;

public static class IdentificationEndpoints
{
    public static void MapIdentificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/species").RequireAuthorization();

        group.MapPost("/identify", async (
            IFormFile image,
            IMediator mediator,
            HttpContext ctx,
            int topK = 5,
            CancellationToken ct = default) =>
        {
            if (image.Length == 0)
                return Results.BadRequest("No image provided.");

            using var ms = new MemoryStream();
            await image.CopyToAsync(ms, ct);
            var imageBase64 = Convert.ToBase64String(ms.ToArray());

            var userId = GetUserIdFromClaims(ctx);
            if (userId == Guid.Empty)
                return Results.Unauthorized();

            var command = new IdentifyFishCommand(imageBase64, topK, userId);
            var result = await mediator.Send(command, ct);

            return Results.Ok(result);
        })
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .WithName("IdentifyFish");
    }

    private static Guid GetUserIdFromClaims(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst("sub")?.Value
               ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
