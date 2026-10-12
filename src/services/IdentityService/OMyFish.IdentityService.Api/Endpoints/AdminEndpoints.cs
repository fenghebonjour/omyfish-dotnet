using MediatR;
using Microsoft.AspNetCore.Mvc;
using OMyFish.IdentityService.Application.Commands;
using OMyFish.IdentityService.Application.Queries;
using OMyFish.IdentityService.Domain.Interfaces;

namespace OMyFish.IdentityService.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/admin/stats", async (IMediator mediator, CancellationToken ct) =>
        {
            var stats = await mediator.Send(new GetStatsQuery(), ct);
            return Results.Ok(new
            {
                users = stats.Users,
                subscriptions = new
                {
                    trialing = stats.Trialing,
                    active = stats.Active,
                    canceled = stats.Canceled,
                    expired = stats.Expired,
                },
                activePlans = new { monthly = stats.ActiveMonthly, yearly = stats.ActiveYearly },
                mrrCad = stats.MrrCad,
            });
        }).RequireAuthorization(policy => policy.RequireRole("ADMIN"));

        app.MapGet("/api/v1/admin/subscriptions", async (IMediator mediator, CancellationToken ct) =>
        {
            var rows = await mediator.Send(new GetAllSubscriptionsQuery(), ct);
            return Results.Ok(rows);
        }).RequireAuthorization(policy => policy.RequireRole("ADMIN"));

        app.MapPost("/api/v1/admin/subscriptions/{userId:guid}/grant", async (
            Guid userId, GrantRequest? req, IMediator mediator, CancellationToken ct) =>
        {
            var sub = await mediator.Send(new GrantSubscriptionCommand(
                userId, req?.Plan ?? "yearly", req?.Days ?? 365), ct);
            return Results.Ok(sub);
        }).RequireAuthorization(policy => policy.RequireRole("ADMIN"));

        app.MapPost("/api/v1/admin/subscriptions/{userId:guid}/revoke", async (
            Guid userId, IMediator mediator, CancellationToken ct) =>
        {
            var sub = await mediator.Send(new RevokeSubscriptionCommand(userId), ct);
            return sub is null ? Results.NotFound() : Results.Ok(sub);
        }).RequireAuthorization(policy => policy.RequireRole("ADMIN"));

        app.MapPost("/api/v1/admin/subscriptions/{userId:guid}/extend-trial", async (
            Guid userId, GrantRequest? req, IMediator mediator, CancellationToken ct) =>
        {
            var sub = await mediator.Send(new ExtendTrialCommand(userId, req?.Days ?? 7), ct);
            return Results.Ok(sub);
        }).RequireAuthorization(policy => policy.RequireRole("ADMIN"));

        app.MapPost("/api/v1/admin/subscriptions/{userId:guid}/refund", async (
            Guid userId, RefundRequest? req, IMediator mediator,
            [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, CancellationToken ct) =>
        {
            try
            {
                var result = await mediator.Send(
                    new RefundCommand(userId, req?.AmountCents, idempotencyKey), ct);
                return result is null
                    ? Results.Problem("Payment processor is not configured or there is nothing to refund.", statusCode: 503)
                    : Results.Ok(new { refundId = result.RefundId, status = result.Status, amountCents = result.AmountCents });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
            catch (IdempotencyConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        }).RequireAuthorization(policy => policy.RequireRole("ADMIN"));

        app.MapPost("/api/v1/admin/subscriptions/reconcile", async (
            long? lookbackHours, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new ReconcileCommand(DateTime.UtcNow.AddHours(-(lookbackHours ?? 24))), ct);
            return Results.Ok(result);
        }).RequireAuthorization(policy => policy.RequireRole("ADMIN"));
    }

    private record GrantRequest(int? Days, string? Plan);
    private record RefundRequest(long? AmountCents);
}
