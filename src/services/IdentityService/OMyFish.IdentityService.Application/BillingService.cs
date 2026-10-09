using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Domain.Interfaces;

namespace OMyFish.IdentityService.Application;

public class BillingService
{
    public const int TrialDays = 7;
    public const double MonthlyCad = 5;
    public const double YearlyCad = 29;

    /// How long an incomplete checkout/refund reservation sits before it's treated as orphaned
    /// rather than "still in progress" — comfortably past the gateway's own worst-case call
    /// time; a reservation only outlives that if the process died before it could complete.
    private static readonly TimeSpan StaleReservationAge = TimeSpan.FromMinutes(1);

    private readonly ISubscriptionRepository _subscriptions;
    private readonly IUserRepository _users;
    private readonly PaymentProcessorRegistry _processors;
    private readonly IIdempotencyKeyRepository _idempotencyKeys;
    private readonly IProcessedWebhookEventRepository _processedWebhookEvents;

    public BillingService(
        ISubscriptionRepository subscriptions, IUserRepository users, PaymentProcessorRegistry processors,
        IIdempotencyKeyRepository idempotencyKeys, IProcessedWebhookEventRepository processedWebhookEvents)
    {
        _subscriptions = subscriptions;
        _users = users;
        _processors = processors;
        _idempotencyKeys = idempotencyKeys;
        _processedWebhookEvents = processedWebhookEvents;
    }

    public async Task<Subscription> StartTrialAsync(Guid userId, CancellationToken ct = default)
    {
        var sub = await _subscriptions.FindByUserIdAsync(userId, ct);
        if (sub is null)
        {
            sub = Subscription.StartTrial(userId, TrialDays);
            await _subscriptions.CreateAsync(sub, ct);
        }
        return sub;
    }

    public Task<Subscription> MySubscriptionAsync(Guid userId, CancellationToken ct = default)
        => StartTrialAsync(userId, ct);

    /// Null when Stripe is not configured.
    public async Task<SubscriptionIntentResult?> StartCheckoutAsync(
        Guid userId, string plan, string idempotencyKey, CancellationToken ct = default)
    {
        if (plan != "monthly" && plan != "yearly")
            throw new ArgumentException("plan must be monthly or yearly");

        var existing = await _idempotencyKeys.FindAsync(idempotencyKey, IdempotencyRecord.Checkout, ct);
        if (existing is not null && !IsOrphaned(existing))
            return await ReplayedCheckoutAsync(RequireOwner(existing, userId), userId, ct);

        var processor = _processors.DefaultProcessor();
        if (processor is null) return null;

        var user = await _users.FindByIdAsync(userId, ct)
            ?? throw new InvalidOperationException("User not found");

        var reservation = existing is not null
            ? RequireOwner(existing, userId)
            : await _idempotencyKeys.ReserveAsync(idempotencyKey, IdempotencyRecord.Checkout, userId, ct);
        try
        {
            var intent = await processor.CreateSubscriptionIntentAsync(userId, user.Email, plan, idempotencyKey, ct);
            if (intent is null)
            {
                await _idempotencyKeys.DeleteAsync(reservation, ct);
                return null;
            }

            var sub = await StartTrialAsync(userId, ct);
            sub.AttachProcessor(intent.Processor, intent.CustomerId, intent.SubscriptionId);
            await _subscriptions.SaveChangesAsync(ct);

            reservation.CompleteCheckout(intent.CustomerId, intent.SubscriptionId, intent.ClientSecret, intent.Status);
            await _idempotencyKeys.SaveAsync(reservation, ct);
            return intent;
        }
        catch
        {
            await _idempotencyKeys.DeleteAsync(reservation, ct);
            throw;
        }
    }

    private async Task<SubscriptionIntentResult> ReplayedCheckoutAsync(
        IdempotencyRecord record, Guid userId, CancellationToken ct)
    {
        if (!record.Completed)
            throw new IdempotencyConflictException("A checkout with this idempotency key is already in progress");
        var sub = await _subscriptions.FindByUserIdAsync(userId, ct);
        return new SubscriptionIntentResult(
            sub?.PaymentProcessor ?? "stripe", record.CustomerId!, record.SubscriptionId!,
            record.ClientSecret, record.CheckoutStatus!);
    }

    /// Null when no payment processor is configured.
    public async Task<SetupIntentResult?> StartPaymentMethodSetupAsync(Guid userId, CancellationToken ct = default)
    {
        var processor = _processors.DefaultProcessor();
        if (processor is null) return null;

        var user = await _users.FindByIdAsync(userId, ct)
            ?? throw new InvalidOperationException("User not found");

        var intent = await processor.CreateSetupIntentAsync(userId, user.Email, ct);
        if (intent is null) return null;

        var sub = await StartTrialAsync(userId, ct);
        sub.AttachProcessorCustomerId(intent.Processor, intent.CustomerId);
        await _subscriptions.SaveChangesAsync(ct);

        return intent;
    }

    /// Throws InvalidOperationException (-> 404) if the user has no subscription/Stripe id on
    /// file. Returns null (-> 503) when the processor isn't configured or there's nothing to
    /// refund — a different failure mode than "not found," same distinction the Java sibling
    /// draws between IllegalArgumentException and IllegalStateException here.
    public async Task<RefundResult?> RefundAsync(
        Guid userId, long? amountCents, string idempotencyKey, CancellationToken ct = default)
    {
        var existing = await _idempotencyKeys.FindAsync(idempotencyKey, IdempotencyRecord.Refund, ct);
        if (existing is not null && !IsOrphaned(existing))
            return ReplayedRefund(RequireOwner(existing, userId));

        var sub = await _subscriptions.FindByUserIdAsync(userId, ct)
            ?? throw new InvalidOperationException("No subscription for that user");
        if (string.IsNullOrEmpty(sub.StripeSubscriptionId))
            throw new InvalidOperationException("No Stripe subscription on file");

        var processor = _processors.ByName(sub.PaymentProcessor ?? "stripe");

        var reservation = existing is not null
            ? RequireOwner(existing, userId)
            : await _idempotencyKeys.ReserveAsync(idempotencyKey, IdempotencyRecord.Refund, userId, ct);
        try
        {
            var result = await processor.RefundSubscriptionAsync(
                sub.StripeSubscriptionId, amountCents, idempotencyKey, sub.LastPaymentReference, ct);
            if (result is null)
            {
                await _idempotencyKeys.DeleteAsync(reservation, ct);
                return null;
            }

            reservation.CompleteRefund(result.RefundId, result.Status, result.AmountCents);
            await _idempotencyKeys.SaveAsync(reservation, ct);
            return result;
        }
        catch
        {
            await _idempotencyKeys.DeleteAsync(reservation, ct);
            throw;
        }
    }

    private static RefundResult ReplayedRefund(IdempotencyRecord record)
    {
        if (!record.Completed)
            throw new IdempotencyConflictException("A refund with this idempotency key is already in progress");
        return new RefundResult(record.RefundId!, record.RefundStatus!, record.AmountCents);
    }

    /// An incomplete reservation past StaleReservationAge can only mean the process that made
    /// the gateway call died before persisting its result — safe to retry with the same
    /// reservation row and the same idempotencyKey, since the processor's own idempotency cache
    /// returns the original response if the call already went through, or performs it now if
    /// it never did.
    private static bool IsOrphaned(IdempotencyRecord record) =>
        !record.Completed && record.IsStale(StaleReservationAge);

    private static IdempotencyRecord RequireOwner(IdempotencyRecord record, Guid userId)
    {
        if (record.UserId != userId)
            throw new ArgumentException("Idempotency key already used");
        return record;
    }

    // Virtual so ReconciliationServiceTest can substitute this concrete class directly, same
    // as the Java sibling mocks BillingService with Mockito (which doesn't need `virtual`).
    public virtual async Task<bool> ApplyEventAsync(PaymentEvent evt, CancellationToken ct = default)
    {
        if (evt.EventId is not null && await _processedWebhookEvents.ExistsAsync(evt.EventId, ct))
            return true;

        var handled = await ApplyEventEffectsAsync(evt, ct);
        if (handled && evt.EventId is not null)
            await _processedWebhookEvents.SaveAsync(ProcessedWebhookEvent.Of(evt.EventId), ct);
        return handled;
    }

    private async Task<bool> ApplyEventEffectsAsync(PaymentEvent evt, CancellationToken ct)
    {
        switch (evt.Type)
        {
            case "subscription_updated":
            case "subscription_deleted":
            {
                if (evt.CustomerId is null) return false;
                var sub = await _subscriptions.FindByStripeCustomerIdAsync(evt.CustomerId, ct);
                if (sub is null) return false;

                if (evt.Type == "subscription_deleted"
                    || evt.ProviderStatus is "canceled" or "unpaid" or "incomplete_expired")
                    sub.Cancel();
                else
                    sub.Activate(sub.Plan ?? "monthly", evt.PeriodEnd, stripeSubscriptionId: evt.SubscriptionId);

                await _subscriptions.SaveChangesAsync(ct);
                return true;
            }
            case "payment_method_attached":
            {
                if (evt.CustomerId is null || evt.PaymentMethodId is null) return false;
                await _processors.ByName(evt.Processor).SetDefaultPaymentMethodAsync(
                    evt.CustomerId, evt.PaymentMethodId, ct);
                return true;
            }
            case "payment_captured":
            {
                if (evt.CustomerId is null || evt.PaymentReference is null) return false;
                var sub = await _subscriptions.FindByStripeCustomerIdAsync(evt.CustomerId, ct);
                if (sub is null) return false;

                sub.RecordLastPaymentReference(evt.PaymentReference);
                await _subscriptions.SaveChangesAsync(ct);
                return true;
            }
            default:
                return false;
        }
    }

    // ── Admin operations ──────────────────────────────────────────────────────

    public Task<IReadOnlyList<Subscription>> AllSubscriptionsAsync(CancellationToken ct = default)
        => _subscriptions.GetAllAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> UserEmailsAsync(CancellationToken ct = default)
        => (await _users.GetAllAsync(ct)).ToDictionary(u => u.Id, u => u.Email);

    public async Task<BillingStats> StatsAsync(CancellationToken ct = default)
    {
        var all = await _subscriptions.GetAllAsync(ct);
        var userCount = await _users.CountAsync(ct);

        int Count(string status) => all.Count(s => s.EffectiveStatus == status);
        var activeMonthly = all.Count(s => s.EffectiveStatus == "active" && s.Plan == "monthly");
        var activeYearly = all.Count(s => s.EffectiveStatus == "active" && s.Plan == "yearly");
        var mrr = Math.Round(activeMonthly * MonthlyCad + activeYearly * YearlyCad / 12, 2);

        return new BillingStats(
            userCount, Count("trialing"), Count("active"), Count("canceled"), Count("expired"),
            activeMonthly, activeYearly, mrr);
    }

    public async Task<Subscription> GrantAsync(Guid userId, string plan, int days, CancellationToken ct = default)
    {
        var sub = await StartTrialAsync(userId, ct);
        sub.Activate(plan, DateTime.UtcNow.AddDays(days));
        await _subscriptions.SaveChangesAsync(ct);
        return sub;
    }

    /// Null when the user has no subscription to revoke.
    public async Task<Subscription?> RevokeAsync(Guid userId, CancellationToken ct = default)
    {
        var sub = await _subscriptions.FindByUserIdAsync(userId, ct);
        if (sub is null) return null;
        sub.Cancel();
        await _subscriptions.SaveChangesAsync(ct);
        return sub;
    }

    public async Task<Subscription> ExtendTrialAsync(Guid userId, int days, CancellationToken ct = default)
    {
        var sub = await StartTrialAsync(userId, ct);
        sub.ExtendTrial(days);
        await _subscriptions.SaveChangesAsync(ct);
        return sub;
    }
}

public sealed record BillingStats(
    int Users, int Trialing, int Active, int Canceled, int Expired,
    int ActiveMonthly, int ActiveYearly, double MrrCad);
