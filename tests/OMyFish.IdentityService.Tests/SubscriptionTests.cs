using OMyFish.IdentityService.Domain.Entities;
using Xunit;

namespace OMyFish.IdentityService.Tests;

public class SubscriptionTests
{
    [Fact]
    public void StartTrial_SevenDayTrialByDefault()
    {
        var sub = Subscription.StartTrial(Guid.NewGuid());

        Assert.Equal("trialing", sub.EffectiveStatus);
        Assert.NotNull(sub.TrialEnd);
        var days = (sub.TrialEnd!.Value - DateTime.UtcNow).TotalDays;
        Assert.InRange(days, 6.9, 7.0);
    }

    [Fact]
    public void ExpiredTrial_ReadsAsExpiredWithoutAWrite()
    {
        var sub = Subscription.StartTrial(Guid.NewGuid(), trialDays: -1);

        Assert.Equal("trialing", sub.Status);
        Assert.Equal("expired", sub.EffectiveStatus);
    }

    [Fact]
    public void Activate_SetsPlanAndStripeIds()
    {
        var sub = Subscription.StartTrial(Guid.NewGuid());
        var periodEnd = DateTime.UtcNow.AddMonths(1);

        sub.Activate("monthly", periodEnd, "cus_123", "sub_456");

        Assert.Equal("active", sub.EffectiveStatus);
        Assert.Equal("monthly", sub.Plan);
        Assert.Equal(periodEnd, sub.CurrentPeriodEnd);
        Assert.Equal("cus_123", sub.StripeCustomerId);
        Assert.Equal("sub_456", sub.StripeSubscriptionId);
    }

    [Fact]
    public void Activate_KeepsExistingStripeIdsWhenNullPassed()
    {
        var sub = Subscription.StartTrial(Guid.NewGuid());
        sub.Activate("monthly", null, "cus_123", "sub_456");

        sub.Activate("monthly", DateTime.UtcNow.AddMonths(1));

        Assert.Equal("cus_123", sub.StripeCustomerId);
        Assert.Equal("sub_456", sub.StripeSubscriptionId);
    }

    [Fact]
    public void Cancel_SetsCanceled()
    {
        var sub = Subscription.StartTrial(Guid.NewGuid());
        sub.Activate("yearly", DateTime.UtcNow.AddYears(1));

        sub.Cancel();

        Assert.Equal("canceled", sub.EffectiveStatus);
    }

    [Fact]
    public void ExtendTrial_FromActiveTrialAddsToTrialEnd()
    {
        var sub = Subscription.StartTrial(Guid.NewGuid());
        var originalEnd = sub.TrialEnd!.Value;

        sub.ExtendTrial(7);

        Assert.Equal(originalEnd.AddDays(7), sub.TrialEnd);
    }

    [Fact]
    public void ExtendTrial_FromExpiredTrialRestartsFromNow()
    {
        var sub = Subscription.StartTrial(Guid.NewGuid(), trialDays: -10);

        sub.ExtendTrial(7);

        Assert.Equal("trialing", sub.EffectiveStatus);
        var days = (sub.TrialEnd!.Value - DateTime.UtcNow).TotalDays;
        Assert.InRange(days, 6.9, 7.0);
    }
}
