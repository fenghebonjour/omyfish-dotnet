using MediatR;
using Microsoft.AspNetCore.Mvc;
using OMyFish.ShopService.Application.Commands;

namespace OMyFish.ShopService.Api.Endpoints;

public static class CheckoutEndpoints
{
    public static void MapCheckoutEndpoints(this RouteGroupBuilder shop)
    {
        shop.MapPost("/checkout", async (
            HttpContext ctx, [FromHeader(Name = "X-Cart-Token")] string? cartToken,
            CheckoutRequest req, IMediator mediator, CancellationToken ct) =>
        {
            var owner = ShopOwnerResolver.Resolve(ctx, cartToken);
            if (owner.IsGuest && string.IsNullOrWhiteSpace(req.GuestEmail))
                return Results.BadRequest(new { error = "guestEmail is required for guest checkout" });

            var result = await mediator.Send(new CheckoutCommand(
                owner, owner.IsGuest ? req.GuestEmail : null, req.SuccessUrl, req.CancelUrl), ct);
            return Results.Ok(new { orderId = result.OrderId, checkoutUrl = result.CheckoutUrl });
        })
        .WithName("Checkout");

        // Public — Stripe calls this directly, no account/JWT involved; signature-verified
        // inside the handler instead. Whitelisted at the gateway.
        shop.MapPost("/webhook/stripe", async (HttpRequest request, IMediator mediator, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var payload = await reader.ReadToEndAsync(ct);
            var headers = request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString() ?? "");
            var handled = await mediator.Send(new ConfirmPaymentWebhookCommand(payload, headers), ct);
            return Results.Ok(new { handled });
        })
        .WithName("StripeWebhook");
    }
}

public sealed record CheckoutRequest(string SuccessUrl, string CancelUrl, string? GuestEmail);
