using System.Security.Claims;
using MediatR;
using OMyFish.ShopService.Application.Commands;
using OMyFish.ShopService.Application.Queries;

namespace OMyFish.ShopService.Api.Endpoints;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this RouteGroupBuilder shop)
    {
        var group = shop.MapGroup("/orders");

        // Public — guest order tracking by orderId + email, no account needed.
        group.MapGet("/lookup", async (Guid orderId, string email, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new LookupGuestOrderQuery(orderId, email), ct)))
            .WithName("LookupGuestOrder");

        // Admin-only; lists every order regardless of owner (newest first) so an admin can find
        // one — e.g. to approve a return — without already having its id from a support request.
        group.MapGet("/admin", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetAllOrdersForAdminQuery(), ct)))
            .RequireAuthorization(policy => policy.RequireRole("ADMIN"))
            .WithName("ListAllOrdersForAdmin");

        group.MapGet("/", async (HttpContext ctx, IMediator mediator, CancellationToken ct) =>
        {
            var userId = GetUserId(ctx);
            if (userId == Guid.Empty) return Results.Unauthorized();
            return Results.Ok(await mediator.Send(new GetOrdersByUserQuery(userId), ct));
        })
        .RequireAuthorization()
        .WithName("ListMyOrders");

        group.MapGet("/{id:guid}", async (Guid id, HttpContext ctx, IMediator mediator, CancellationToken ct) =>
        {
            var userId = GetUserId(ctx);
            if (userId == Guid.Empty) return Results.Unauthorized();
            return Results.Ok(await mediator.Send(new GetOrderByIdQuery(id, userId), ct));
        })
        .RequireAuthorization()
        .WithName("GetMyOrder");

        group.MapPost("/{id:guid}/returns", async (
            Guid id, HttpContext ctx, ReturnRequest req, IMediator mediator, CancellationToken ct) =>
        {
            var userId = GetUserId(ctx);
            if (userId == Guid.Empty) return Results.Unauthorized();
            return Results.Ok(await mediator.Send(new RequestReturnCommand(id, userId, req.Reason), ct));
        })
        .RequireAuthorization()
        .WithName("RequestReturn");

        group.MapPatch("/{id:guid}/returns/approve", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ApproveReturnCommand(id), ct)))
            .RequireAuthorization(policy => policy.RequireRole("ADMIN"))
            .WithName("ApproveReturn");
    }

    private static Guid GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst("sub")?.Value ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

public sealed record ReturnRequest(string? Reason);
