using MediatR;
using Prometheus;
using Microsoft.AspNetCore.Mvc;
using OMyFish.ShopService.Application.Commands;

namespace OMyFish.ShopService.Api.Endpoints;

public static class CheckoutEndpoints
{
    // new / replayed / rejected — "rejected" covers every attempt that didn't produce an order
    // (out of stock, idempotency conflict, processor failure or open circuit, kill switch,
    // validation), same buckets as omyfish-java's MeteredCheckoutUseCase. Rate-limited (429)
    // requests never reach this handler; they show up in http_requests_received_total.
    private static readonly Counter CheckoutRequests = Metrics.CreateCounter(
        "shop_checkout_requests_total", "Checkout attempts by outcome.",
        new CounterConfiguration { LabelNames = ["result"] });

    public static void MapCheckoutEndpoints(this RouteGroupBuilder shop)
    {
        shop.MapPost("/checkout", async (
            HttpContext ctx, [FromHeader(Name = "X-Cart-Token")] string? cartToken,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CheckoutRequest req, IMediator mediator, FeatureFlags flags, CancellationToken ct) =>
        {
            var outcome = "rejected";
            try
            {
                if (!flags.CheckoutEnabled)
                    return Results.Problem("Checkout is temporarily paused", statusCode: StatusCodes.Status503ServiceUnavailable);

                // Required, so a client retry (timeout, double-click) replays the first attempt
                // instead of reserving stock and creating a second order.
                if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 255)
                    return Results.BadRequest(new { error = "Idempotency-Key header is required (max 255 chars)" });

                var owner = ShopOwnerResolver.Resolve(ctx, cartToken);
                if (owner.IsGuest && string.IsNullOrWhiteSpace(req.GuestEmail))
                    return Results.BadRequest(new { error = "guestEmail is required for guest checkout" });

                var result = await mediator.Send(new CheckoutCommand(
                    owner, owner.IsGuest ? req.GuestEmail : null, req.SuccessUrl, req.CancelUrl, idempotencyKey), ct);
                outcome = result.Replayed ? "replayed" : "new";
                return Results.Ok(new { orderId = result.OrderId, checkoutUrl = result.CheckoutUrl });
            }
            finally
            {
                // A thrown exception (mapped to its status by ShopExceptionFilter) stays "rejected".
                CheckoutRequests.WithLabels(outcome).Inc();
            }
        })
        .RequireRateLimiting("checkout")
        .WithName("Checkout");

        // Public — Stripe/PayPal/Adyen call this directly, no account/JWT involved;
        // signature-verified inside the handler instead. Whitelisted at the gateway.
        shop.MapPost("/webhook/{processor}", async (
            string processor, HttpRequest request, IMediator mediator, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var payload = await reader.ReadToEndAsync(ct);
            var headers = request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString() ?? "");
            var handled = await mediator.Send(new ConfirmPaymentWebhookCommand(processor, payload, headers), ct);
            return Results.Ok(new { handled });
        })
        .WithName("PaymentWebhook");
    }
}

public sealed record CheckoutRequest(string SuccessUrl, string CancelUrl, string? GuestEmail);
