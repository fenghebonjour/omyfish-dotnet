using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OMyFish.ShopService.Application.Commands;

namespace OMyFish.ShopService.Api.Endpoints;

public static class CartEndpoints
{
    public static void MapCartEndpoints(this RouteGroupBuilder shop)
    {
        var group = shop.MapGroup("/cart");

        group.MapGet("/", async (
            HttpContext ctx, [FromHeader(Name = "X-Cart-Token")] string? cartToken,
            IMediator mediator, CancellationToken ct) =>
        {
            var owner = ShopOwnerResolver.Resolve(ctx, cartToken);
            return Results.Ok(await mediator.Send(new GetOrCreateCartCommand(owner), ct));
        })
        .WithName("ViewCart");

        group.MapPost("/items", async (
            HttpContext ctx, [FromHeader(Name = "X-Cart-Token")] string? cartToken,
            AddCartItemRequest req, IMediator mediator, CancellationToken ct) =>
        {
            var owner = ShopOwnerResolver.Resolve(ctx, cartToken);
            return Results.Ok(await mediator.Send(new AddToCartCommand(owner, req.ProductId, req.Quantity), ct));
        })
        .WithName("AddCartItem");

        group.MapPut("/items/{productId:guid}", async (
            Guid productId, HttpContext ctx, [FromHeader(Name = "X-Cart-Token")] string? cartToken,
            UpdateCartItemRequest req, IMediator mediator, CancellationToken ct) =>
        {
            var owner = ShopOwnerResolver.Resolve(ctx, cartToken);
            return Results.Ok(await mediator.Send(new UpdateCartItemCommand(owner, productId, req.Quantity), ct));
        })
        .WithName("UpdateCartItem");

        // Authenticated only — folds the guest cart named by request.GuestToken into the
        // caller's account cart. Called by the frontend right after login/register when a
        // guest cart token is still in storage.
        group.MapPost("/merge", async (HttpContext ctx, MergeCartRequest req, IMediator mediator, CancellationToken ct) =>
        {
            var userId = GetUserId(ctx);
            if (userId == Guid.Empty) return Results.Unauthorized();
            return Results.Ok(await mediator.Send(new MergeGuestCartCommand(userId, req.GuestToken), ct));
        })
        .RequireAuthorization()
        .WithName("MergeGuestCart");
    }

    private static Guid GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst("sub")?.Value ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

public sealed record AddCartItemRequest(Guid ProductId, int Quantity);
public sealed record UpdateCartItemRequest(int Quantity);
public sealed record MergeCartRequest(Guid GuestToken);
