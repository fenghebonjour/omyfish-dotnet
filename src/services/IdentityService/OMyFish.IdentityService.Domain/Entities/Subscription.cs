namespace OMyFish.IdentityService.Domain.Entities;

public sealed class Subscription
{
    private Subscription() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Status { get; private set; } = "trialing"; // trialing|active|past_due|canceled|expired
    public string? Plan { get; private set; }                // monthly|yearly
    public DateTime? TrialEnd { get; private set; }
    public DateTime? CurrentPeriodEnd { get; private set; }
    public string? StripeCustomerId { get; private set; }
    public string? StripeSubscriptionId { get; private set; }
    public string? PaymentProcessor { get; private set; } // stripe | paypal | adyen
    /// Adyen has no subscription object, so refunding it needs the pspReference of the last
    /// captured payment rather than a subscription id — unused by Stripe/PayPal, which look
    /// up the latest payment from their own subscription object instead.
    public string? LastPaymentReference { get; private set; }
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

    /// Records a processor's ids as soon as they're known, without changing status.
    public void AttachProcessor(string processor, string customerId, string subscriptionId)
    {
        PaymentProcessor = processor;
        StripeCustomerId = customerId;
        StripeSubscriptionId = subscriptionId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// Records the processor customer id alone, e.g. after tokenizing a payment method
    /// pre-checkout, when there's no subscription id yet.
    public void AttachProcessorCustomerId(string processor, string customerId)
    {
        PaymentProcessor = processor;
        StripeCustomerId = customerId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// Stripe keeps retrying a failed renewal for days before actually canceling —
    /// collapsing straight to Cancel() here would cut off a still-maybe-paying customer
    /// too early.
    public void MarkPastDue()
    {
        Status = "past_due";
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecordLastPaymentReference(string reference)
    {
        LastPaymentReference = reference;
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
