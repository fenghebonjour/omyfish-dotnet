using MediatR;
using OMyFish.ShopService.Application.Commands;
using OMyFish.ShopService.Application.Interfaces;

namespace OMyFish.ShopService.Api;

/// Cancels and restocks any order still PendingPayment past a timeout. Unlike Stripe's
/// checkout.session.expired, neither PayPal Orders v2 nor Adyen Sessions fire a webhook for an
/// abandoned checkout — this sweep covers all three processors uniformly, and also covers a
/// dropped Stripe expiry webhook. Reuses CancelOrderCommand, no new restock logic. Port of
/// omyfish-java's StaleOrderSweepJob, with the same single-instance assumption: a second
/// replica does nothing harmful (cancelling an already-cancelled order just throws
/// InvalidOrderStateException, logged and skipped), just redundant work.
public sealed class StaleOrderSweepService(
    IServiceScopeFactory scopes, IConfiguration config, ILogger<StaleOrderSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeout = TimeSpan.FromMinutes(config.GetValue("Shop:StaleOrderSweep:TimeoutMinutes", 180));
        var interval = TimeSpan.FromMinutes(config.GetValue("Shop:StaleOrderSweep:IntervalMinutes", 30));

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await SweepAsync(DateTime.UtcNow - timeout, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Stale-order sweep failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task SweepAsync(DateTime cutoff, CancellationToken ct)
    {
        IReadOnlyList<Guid> stale;
        using (var scope = scopes.CreateScope())
            stale = await scope.ServiceProvider.GetRequiredService<IOrderRepository>()
                .GetPendingPaymentIdsOlderThanAsync(cutoff, ct);

        foreach (var orderId in stale)
        {
            // Fresh scope (and DbContext) per order, so one failure can't poison the rest.
            using var scope = scopes.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CancelOrderCommand(orderId), ct);
                logger.LogInformation("Cancelled stale pending-payment order {OrderId}", orderId);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Could not cancel stale order {OrderId}", orderId);
            }
        }
    }
}
