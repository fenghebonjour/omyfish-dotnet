using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OMyFish.IdentityService.Application;
using OMyFish.IdentityService.Application.Commands;
using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.IdentityService.Application.Queries;
using OMyFish.IdentityService.Domain.Interfaces;

namespace OMyFish.IdentityService.Api.Endpoints;

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/billing/me", async (ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Results.Unauthorized();
            var sub = await mediator.Send(new GetMySubscriptionQuery(userId), ct);
            return Results.Ok(sub);
        }).RequireAuthorization();

        app.MapPost("/api/v1/billing/checkout", async (
            CheckoutRequest req, ClaimsPrincipal principal, IMediator mediator,
            [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, CancellationToken ct) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Results.Unauthorized();
            try
            {
                var intent = await mediator.Send(new StartCheckoutCommand(userId, req.Plan, idempotencyKey), ct);
                return intent is null
                    ? Results.Problem("No payment processor is configured.", statusCode: 503)
                    : Results.Ok(new CheckoutResponse(
                        intent.Processor, intent.ClientSecret, intent.SubscriptionId, intent.Status));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
            catch (IdempotencyConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (AlreadySubscribedException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        }).RequireAuthorization();

        app.MapPost("/api/v1/billing/payment-method/setup", async (
            ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Results.Unauthorized();
            var intent = await mediator.Send(new StartPaymentMethodSetupCommand(userId), ct);
            return intent is null
                ? Results.Problem("No payment processor is configured.", statusCode: 503)
                : Results.Ok(new SetupIntentResponse(intent.Processor, intent.CustomerId, intent.ClientSecret));
        }).RequireAuthorization();

        app.MapPost("/api/v1/billing/portal-session", async (
            PortalSessionRequest req, ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Results.Unauthorized();
            try
            {
                var url = await mediator.Send(new CreatePortalSessionCommand(userId, req.ReturnUrl), ct);
                return url is null
                    ? Results.Problem("No self-service portal is available for this payment processor.", statusCode: 503)
                    : Results.Ok(new PortalSessionResponse(url));
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
        }).RequireAuthorization();

        app.MapPost("/api/v1/billing/webhook/{processor}", async (
            string processor, HttpRequest request, PaymentProcessorRegistry processors,
            IMediator mediator, CancellationToken ct) =>
        {
            if (!processors.Exists(processor))
                return Results.NotFound(new { error = $"Unknown payment processor: {processor}" });

            IPaymentGateway gateway;
            try
            {
                gateway = processors.ByName(processor);
            }
            catch (InvalidOperationException)
            {
                return Results.Problem($"Payment processor is not configured: {processor}", statusCode: 503);
            }

            var headers = request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString());
            var payload = await new StreamReader(request.Body).ReadToEndAsync(ct);
            var evt = await gateway.VerifyWebhookAsync(payload, headers, ct);
            if (evt is null)
                return Results.BadRequest(new { error = "Invalid webhook signature" });

            var handled = await mediator.Send(new ApplyPaymentEventCommand(evt), ct);
            return Results.Ok(new { handled });
        });
    }

    private record CheckoutRequest(string Plan);
    private record CheckoutResponse(string Processor, string? ClientSecret, string SubscriptionId, string Status);
    private record SetupIntentResponse(string Processor, string CustomerId, string ClientSecret);
    private record PortalSessionRequest(string ReturnUrl);
    private record PortalSessionResponse(string Url);
}
