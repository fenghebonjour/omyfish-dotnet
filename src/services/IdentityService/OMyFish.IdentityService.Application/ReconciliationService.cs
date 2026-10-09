using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Domain.Interfaces;

namespace OMyFish.IdentityService.Application;

/// Repairs a local Subscription row whose processor-id link never got saved — e.g. a crash
/// between a gateway call succeeding in BillingService.StartCheckoutAsync/
/// StartPaymentMethodSetupAsync and SaveChangesAsync() persisting the id. Driven from each
/// configured gateway's own subscription list (keyed by the user_id metadata every checkout
/// already attaches), not by scanning local rows for a missing link, since a null link is also
/// just the normal state for any trialing user who hasn't subscribed yet.
///
/// Single-instance assumption: no claim/lock step, so a second replica would run this
/// concurrently. Low-stakes either way, since relinking + resyncing status is idempotent.
public class ReconciliationService
{
    private readonly PaymentProcessorRegistry _processors;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly BillingService _billing;

    public ReconciliationService(
        PaymentProcessorRegistry processors, ISubscriptionRepository subscriptions, BillingService billing)
    {
        _processors = processors;
        _subscriptions = subscriptions;
        _billing = billing;
    }

    public async Task<ReconciliationResult> ReconcileAsync(DateTime since, CancellationToken ct = default)
    {
        var repaired = new List<string>();
        var errors = new List<string>();
        var checked_ = 0;

        foreach (var processor in _processors.Configured())
        {
            IReadOnlyList<ReconciliationCandidate> candidates;
            try
            {
                candidates = await processor.ListRecentSubscriptionsAsync(since, ct);
            }
            catch (Exception e)
            {
                errors.Add($"{processor.Name}: failed to list subscriptions — {e.Message}");
                continue;
            }

            foreach (var c in candidates)
            {
                checked_++;
                if (c.UserId is null)
                {
                    errors.Add($"{c.Processor} subscription {c.SubscriptionId} has no usable user_id metadata, skipped");
                    continue;
                }
                try
                {
                    if (await RelinkAsync(c, ct))
                        repaired.Add($"{c.Processor}:{c.SubscriptionId} -> user {c.UserId}");
                    await ResyncStatusAsync(c, ct);
                }
                catch (Exception e)
                {
                    errors.Add($"{c.Processor} subscription {c.SubscriptionId}: {e.Message}");
                }
            }
        }

        return new ReconciliationResult(checked_, repaired, errors);
    }

    /// Returns true if the local row's link was actually missing/mismatched and got repaired.
    private async Task<bool> RelinkAsync(ReconciliationCandidate c, CancellationToken ct)
    {
        var sub = await _subscriptions.FindByUserIdAsync(c.UserId!.Value, ct);
        if (sub is null)
        {
            sub = Subscription.StartTrial(c.UserId.Value, 0);
            await _subscriptions.CreateAsync(sub, ct);
        }

        var mismatched = sub.StripeCustomerId != c.CustomerId || sub.StripeSubscriptionId != c.SubscriptionId;
        if (mismatched)
        {
            sub.AttachProcessor(c.Processor, c.CustomerId, c.SubscriptionId);
            await _subscriptions.SaveChangesAsync(ct);
        }
        return mismatched;
    }

    /// Re-syncs status/periodEnd via the already-tested webhook-effect path, now that the link
    /// is guaranteed to resolve. No EventId set, so ApplyEventAsync's dedup check doesn't apply
    /// — safe, since the subscription_updated case just re-asserts current status.
    private Task ResyncStatusAsync(ReconciliationCandidate c, CancellationToken ct) =>
        _billing.ApplyEventAsync(new PaymentEvent(
            "subscription_updated", null, c.CustomerId, c.SubscriptionId, c.Plan, c.ProviderStatus, c.PeriodEnd,
            Processor: c.Processor), ct);
}

public sealed record ReconciliationResult(int Checked, IReadOnlyList<string> Repaired, IReadOnlyList<string> Errors);
