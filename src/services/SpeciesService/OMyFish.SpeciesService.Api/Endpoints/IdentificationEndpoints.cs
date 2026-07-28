using MediatR;
using OMyFish.SpeciesService.Application.Commands;

namespace OMyFish.SpeciesService.Api.Endpoints;

public static class IdentificationEndpoints
{
    public static void MapIdentificationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/species/identify", async (
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
            var imageBytes = ms.ToArray();

            var userId = GetUserIdFromClaims(ctx);
            var command = new IdentifyFishCommand(imageBytes, image.FileName, image.ContentType, topK, userId);
            try
            {
                var result = await mediator.Send(command, ct);
                return Results.Ok(result);
            }
            catch (HttpRequestException)
            {
                return Results.Problem(
                    title: "AI service unavailable",
                    detail: "The species identification service is unreachable or still starting. Try again shortly.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .AllowAnonymous()
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
