using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Domain.Interfaces;

namespace OMyFish.IdentityService.Application;

public class BillingService
{
    public const int TrialDays = 7;
    public const double MonthlyCad = 5;
    public const double YearlyCad = 29;

    private readonly ISubscriptionRepository _subscriptions;
    private readonly IUserRepository _users;
    private readonly IPaymentGateway _payments;

    public BillingService(
        ISubscriptionRepository subscriptions, IUserRepository users, IPaymentGateway payments)
    {
        _subscriptions = subscriptions;
        _users = users;
        _payments = payments;
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
    public async Task<string?> CheckoutUrlAsync(Guid userId, string plan, CancellationToken ct = default)
    {
        if (plan != "monthly" && plan != "yearly")
            throw new ArgumentException("plan must be monthly or yearly");

        var user = await _users.FindByIdAsync(userId, ct)
            ?? throw new InvalidOperationException("User not found");

        return await _payments.CreateCheckoutUrlAsync(userId, user.Email, plan, ct);
    }

    public async Task<bool> ApplyEventAsync(PaymentEvent evt, CancellationToken ct = default)
    {
        switch (evt.Type)
        {
            case "checkout_completed":
            {
                if (!Guid.TryParse(evt.ClientReferenceId, out var userId)) return false;
                var sub = await StartTrialAsync(userId, ct);
                // Authoritative period end arrives on subscription_updated.
                sub.Activate(evt.Plan ?? "monthly", null, evt.CustomerId, evt.SubscriptionId);
                await _subscriptions.SaveChangesAsync(ct);
                return true;
            }
            case "subscription_updated":
            case "subscription_deleted":
            {
                if (evt.CustomerId is null) return false;
                var sub = await _subscriptions.FindByStripeCustomerIdAsync(evt.CustomerId, ct);
                if (sub is null) return false;

                if (evt.Type == "subscription_deleted"
                    || evt.ProviderStatus is "canceled" or "unpaid")
                    sub.Cancel();
                else
                    sub.Activate(sub.Plan ?? "monthly", evt.PeriodEnd, stripeSubscriptionId: evt.SubscriptionId);

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
