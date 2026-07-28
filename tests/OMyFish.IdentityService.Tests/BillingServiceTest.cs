using NSubstitute;
using OMyFish.IdentityService.Application;
using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Domain.Interfaces;
using Xunit;

namespace OMyFish.IdentityService.Tests;

public class BillingServiceTest
{
    private readonly ISubscriptionRepository _subscriptions = Substitute.For<ISubscriptionRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPaymentGateway _payments = Substitute.For<IPaymentGateway>();
    private readonly BillingService _billing;

    private static readonly Guid UserId = Guid.NewGuid();

    public BillingServiceTest()
    {
        _billing = new BillingService(_subscriptions, _users, _payments);
    }

    [Fact]
    public async Task StartTrialAsync_ExistingSubscription_ReturnsItWithoutCreating()
    {
        var existing = Subscription.StartTrial(UserId, 7);
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _billing.StartTrialAsync(UserId);

        Assert.Same(existing, result);
        await _subscriptions.DidNotReceive().CreateAsync(Arg.Any<Subscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartTrialAsync_NoExistingSubscription_CreatesOne()
    {
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((Subscription?)null);

        var result = await _billing.StartTrialAsync(UserId);

        Assert.Equal("trialing", result.EffectiveStatus);
        await _subscriptions.Received(1).CreateAsync(Arg.Any<Subscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckoutUrlAsync_InvalidPlan_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _billing.CheckoutUrlAsync(UserId, "lifetime"));
    }

    [Fact]
    public async Task CheckoutUrlAsync_UnknownUser_Throws()
    {
        _users.FindByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _billing.CheckoutUrlAsync(UserId, "monthly"));
    }

    [Fact]
    public async Task CheckoutUrlAsync_ValidPlan_DelegatesToGateway()
    {
        var user = User.Create("a@b.c", "hash");
        _users.FindByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _payments.CreateCheckoutUrlAsync(UserId, "a@b.c", "yearly", Arg.Any<CancellationToken>())
            .Returns("https://stripe.example/checkout/abc");

        var url = await _billing.CheckoutUrlAsync(UserId, "yearly");

        Assert.Equal("https://stripe.example/checkout/abc", url);
    }

    [Fact]
    public async Task ApplyEventAsync_CheckoutCompleted_ActivatesSubscription()
    {
        var sub = Subscription.StartTrial(UserId, 7);
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);

        var handled = await _billing.ApplyEventAsync(new PaymentEvent(
            "checkout_completed", UserId.ToString(), "cus_123", "sub_456", "yearly", null, null));

        Assert.True(handled);
        Assert.Equal("active", sub.EffectiveStatus);
        Assert.Equal("yearly", sub.Plan);
        Assert.Equal("cus_123", sub.StripeCustomerId);
    }

    [Fact]
    public async Task ApplyEventAsync_SubscriptionDeleted_Cancels()
    {
        var sub = Subscription.StartTrial(UserId, 7);
        sub.Activate("monthly", null, "cus_123", "sub_456");
        _subscriptions.FindByStripeCustomerIdAsync("cus_123", Arg.Any<CancellationToken>()).Returns(sub);

        var handled = await _billing.ApplyEventAsync(new PaymentEvent(
            "subscription_deleted", null, "cus_123", "sub_456", null, null, null));

        Assert.True(handled);
        Assert.Equal("canceled", sub.EffectiveStatus);
    }

    [Fact]
    public async Task ApplyEventAsync_SubscriptionUpdated_RefreshesPeriodEnd()
    {
        var sub = Subscription.StartTrial(UserId, 7);
        sub.Activate("monthly", null, "cus_123", "sub_456");
        _subscriptions.FindByStripeCustomerIdAsync("cus_123", Arg.Any<CancellationToken>()).Returns(sub);
        var periodEnd = DateTime.UtcNow.AddDays(30);

        await _billing.ApplyEventAsync(new PaymentEvent(
            "subscription_updated", null, "cus_123", "sub_456", null, "active", periodEnd));

        Assert.Equal(periodEnd, sub.CurrentPeriodEnd);
        Assert.Equal("active", sub.EffectiveStatus);
    }

    [Fact]
    public async Task ApplyEventAsync_UnknownCustomer_ReturnsFalse()
    {
        _subscriptions.FindByStripeCustomerIdAsync("cus_ghost", Arg.Any<CancellationToken>())
            .Returns((Subscription?)null);

        var handled = await _billing.ApplyEventAsync(new PaymentEvent(
            "subscription_updated", null, "cus_ghost", null, null, "active", null));

        Assert.False(handled);
    }

    [Fact]
    public async Task ApplyEventAsync_UnrecognizedType_ReturnsFalse()
    {
        var handled = await _billing.ApplyEventAsync(new PaymentEvent(
            "payment_intent.succeeded", null, null, null, null, null, null));

        Assert.False(handled);
    }

    [Fact]
    public async Task StatsAsync_ComputesMrrFromActivePlans()
    {
        var monthly = Subscription.StartTrial(Guid.NewGuid(), 7);
        monthly.Activate("monthly", null);
        var yearly = Subscription.StartTrial(Guid.NewGuid(), 7);
        yearly.Activate("yearly", null);
        var trialing = Subscription.StartTrial(Guid.NewGuid(), 7);
        _subscriptions.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Subscription> { monthly, yearly, trialing });
        _users.CountAsync(Arg.Any<CancellationToken>()).Returns(3);

        var stats = await _billing.StatsAsync();

        Assert.Equal(2, stats.Active);
        Assert.Equal(1, stats.Trialing);
        Assert.Equal(Math.Round(5 + 29 / 12.0, 2), stats.MrrCad);
    }

    [Fact]
    public async Task GrantAsync_ActivatesSubscriptionWithFuturePeriodEnd()
    {
        var sub = Subscription.StartTrial(UserId, 7);
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);

        var granted = await _billing.GrantAsync(UserId, "yearly", 365);

        Assert.Equal("active", granted.EffectiveStatus);
        Assert.True(granted.CurrentPeriodEnd > DateTime.UtcNow);
    }

    [Fact]
    public async Task RevokeAsync_NoExistingSubscription_ReturnsNull()
    {
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((Subscription?)null);

        var result = await _billing.RevokeAsync(UserId);

        Assert.Null(result);
    }

    [Fact]
    public async Task RevokeAsync_ExistingSubscription_Cancels()
    {
        var sub = Subscription.StartTrial(UserId, 7);
        sub.Activate("monthly", null);
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);

        var result = await _billing.RevokeAsync(UserId);

        Assert.NotNull(result);
        Assert.Equal("canceled", result!.EffectiveStatus);
    }

    [Fact]
    public async Task ExtendTrialAsync_PushesTrialEndForward()
    {
        var sub = Subscription.StartTrial(UserId, 7);
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);
        var originalTrialEnd = sub.TrialEnd;

        var extended = await _billing.ExtendTrialAsync(UserId, 7);

        Assert.True(extended.TrialEnd > originalTrialEnd);
    }
}
