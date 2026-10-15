using MediatR;
using OMyFish.ShopService.Application.Commands;

namespace OMyFish.ShopService.Api.Endpoints;

// Admin-only, and deliberately outside "/products" — that prefix is public for catalog
// browsing, but this write path must still require a valid JWT + ADMIN role.
public static class AdminProductEndpoints
{
    public static void MapAdminProductEndpoints(this RouteGroupBuilder shop)
    {
        shop.MapPost("/admin/products/{id:guid}/image", async (Guid id, IFormFile image, IMediator mediator, CancellationToken ct) =>
        {
            using var ms = new MemoryStream();
            await image.CopyToAsync(ms, ct);
            ms.Position = 0;

            var result = await mediator.Send(
                new UploadProductImageCommand(id, ms, ms.Length, image.ContentType, image.FileName), ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(policy => policy.RequireRole("ADMIN"))
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .WithName("UploadProductImage");
    }
}
