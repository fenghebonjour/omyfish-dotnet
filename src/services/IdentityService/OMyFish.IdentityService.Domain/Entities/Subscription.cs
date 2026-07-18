namespace OMyFish.IdentityService.Domain.Entities;

public sealed class Subscription
{
    private Subscription() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Status { get; private set; } = "trialing"; // trialing|active|canceled|expired
    public string? Plan { get; private set; }                // monthly|yearly
    public DateTime? TrialEnd { get; private set; }
    public DateTime? CurrentPeriodEnd { get; private set; }
    public string? StripeCustomerId { get; private set; }
    public string? StripeSubscriptionId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// A trial past its end date reads as expired without needing a write.
    public string EffectiveStatus =>
        Status == "trialing" && TrialEnd is not null && TrialEnd < DateTime.UtcNow
            ? "expired" : Status;

    public static Subscription StartTrial(Guid userId, int trialDays = 7)
    {
        return new Subscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = "trialing",
            TrialEnd = DateTime.UtcNow.AddDays(trialDays),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    public void Activate(string plan, DateTime? periodEnd,
        string? stripeCustomerId = null, string? stripeSubscriptionId = null)
    {
        Status = "active";
        Plan = plan;
        CurrentPeriodEnd = periodEnd;
        StripeCustomerId = stripeCustomerId ?? StripeCustomerId;
        StripeSubscriptionId = stripeSubscriptionId ?? StripeSubscriptionId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        Status = "canceled";
        UpdatedAt = DateTime.UtcNow;
    }

    public void ExtendTrial(int days)
    {
        var baseline = TrialEnd is not null && TrialEnd > DateTime.UtcNow
            ? TrialEnd.Value : DateTime.UtcNow;
        Status = "trialing";
        TrialEnd = baseline.AddDays(days);
        UpdatedAt = DateTime.UtcNow;
    }
}
