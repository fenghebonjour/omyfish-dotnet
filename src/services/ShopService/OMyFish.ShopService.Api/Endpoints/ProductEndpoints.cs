using MediatR;
using OMyFish.ShopService.Application.Queries;

namespace OMyFish.ShopService.Api.Endpoints;

// Public catalog browsing — whitelisted at the gateway, so no account needed.
public static class ProductEndpoints
{
    public static void MapProductEndpoints(this RouteGroupBuilder shop)
    {
        var group = shop.MapGroup("/products");

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetProductsQuery(), ct)))
            .WithName("ListProducts");

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetProductByIdQuery(id), ct)))
            .WithName("GetProduct");

        // Serves an uploaded product photo (seeded placeholders never hit this — their
        // ImageUrl is an inline data: URI instead). Uploading is a separate, admin-only,
        // non-public endpoint — see AdminProductEndpoints.
        group.MapGet("/{id:guid}/image", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var image = await mediator.Send(new GetProductImageQuery(id), ct);
            return Results.Stream(image.Data, image.ContentType);
        })
        .WithName("GetProductImage");
    }
}
