using MediatR;
using Microsoft.AspNetCore.RateLimiting;
using OMyFish.SpeciesService.Application.Commands;

namespace OMyFish.SpeciesService.Api.Endpoints;

public static class IdentificationEndpoints
{
    private static readonly HashSet<string> AllowedContentTypes =
        ["image/jpeg", "image/png", "image/webp", "image/heic", "image/heif"];
    private const long MaxImageBytes = 20L * 1024 * 1024;
    private const int MaxTopK = 25;

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
            if (image.Length > MaxImageBytes)
                return Results.Problem("Image must be 20MB or smaller.", statusCode: StatusCodes.Status413PayloadTooLarge);
            var mediaType = image.ContentType?.Split(';')[0].Trim().ToLowerInvariant();
            if (mediaType is null || !AllowedContentTypes.Contains(mediaType))
                return Results.Problem("Unsupported image type.", statusCode: StatusCodes.Status415UnsupportedMediaType);

            using var ms = new MemoryStream();
            await image.CopyToAsync(ms, ct);
            var imageBytes = ms.ToArray();

            var userId = GetUserIdFromClaims(ctx);
            var command = new IdentifyFishCommand(imageBytes, image.FileName, image.ContentType, Math.Clamp(topK, 1, MaxTopK), userId);
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
        // Anonymous + AI-backed = capped per IP so it can't be scripted for free unbounded
        // use (BACKLOG.md item F, WEAKNESS_AUDIT.md §1.2).
        .RequireRateLimiting("identify")
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
