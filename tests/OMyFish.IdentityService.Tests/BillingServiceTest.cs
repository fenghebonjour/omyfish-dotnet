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
    private readonly IIdempotencyKeyRepository _idempotencyKeys = Substitute.For<IIdempotencyKeyRepository>();
    private readonly IProcessedWebhookEventRepository _processedWebhookEvents =
        Substitute.For<IProcessedWebhookEventRepository>();
    private readonly PaymentProcessorRegistry _processors;
    private readonly BillingService _billing;

    private const string IdempotencyKey = "idem-key-1";
    private static readonly Guid UserId = Guid.NewGuid();

    public BillingServiceTest()
    {
        _payments.Name.Returns("stripe");
        _payments.IsConfigured.Returns(true);
        _processors = new PaymentProcessorRegistry([_payments], "stripe");
        _billing = new BillingService(_subscriptions, _users, _processors, _idempotencyKeys, _processedWebhookEvents);
        // No existing reservation by default; ReserveAsync hands back a fresh record built
        // from whatever args the call used, same as a real first-time reservation would.
        _idempotencyKeys.ReserveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(info => IdempotencyRecord.Reserve((string)info[0], (string)info[1], (Guid)info[2]));
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
    public async Task StartCheckoutAsync_InvalidPlan_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _billing.StartCheckoutAsync(UserId, "lifetime", IdempotencyKey));
    }

    [Fact]
    public async Task StartCheckoutAsync_UnknownUser_Throws()
    {
        _users.FindByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _billing.StartCheckoutAsync(UserId, "monthly", IdempotencyKey));
    }

    [Fact]
    public async Task StartCheckoutAsync_ValidPlan_AttachesProcessorIdsWithoutActivating()
    {
        var user = User.Create("a@b.c", "hash");
        var sub = Subscription.StartTrial(UserId, 7);
        _users.FindByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);
        _payments.CreateSubscriptionIntentAsync(UserId, "a@b.c", "yearly", IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionIntentResult("stripe", "cus_123", "sub_456", "secret_abc", "incomplete"));

        var intent = await _billing.StartCheckoutAsync(UserId, "yearly", IdempotencyKey);

        Assert.NotNull(intent);
        Assert.Equal("secret_abc", intent!.ClientSecret);
        Assert.Equal("cus_123", sub.StripeCustomerId);
        Assert.Equal("sub_456", sub.StripeSubscriptionId);
        Assert.Equal("trialing", sub.EffectiveStatus);
    }

    [Fact]
    public async Task StartCheckoutAsync_GatewayNotConfigured_ReturnsNull()
    {
        var user = User.Create("a@b.c", "hash");
        var sub = Subscription.StartTrial(UserId, 7);
        _users.FindByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);
        _payments.CreateSubscriptionIntentAsync(UserId, "a@b.c", "monthly", IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns((SubscriptionIntentResult?)null);

        var intent = await _billing.StartCheckoutAsync(UserId, "monthly", IdempotencyKey);

        Assert.Null(intent);
    }

    [Fact]
    public async Task StartCheckoutAsync_CompletedIdempotencyKey_ReplaysWithoutCallingGateway()
    {
        var record = IdempotencyRecord.Reserve(IdempotencyKey, IdempotencyRecord.Checkout, UserId);
        record.CompleteCheckout("cus_123", "sub_456", "secret_abc", "incomplete");
        _idempotencyKeys.FindAsync(IdempotencyKey, IdempotencyRecord.Checkout, Arg.Any<CancellationToken>())
            .Returns(record);

        var intent = await _billing.StartCheckoutAsync(UserId, "yearly", IdempotencyKey);

        Assert.Equal("secret_abc", intent!.ClientSecret);
        await _payments.DidNotReceive().CreateSubscriptionIntentAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartCheckoutAsync_InProgressIdempotencyKey_ThrowsConflict()
    {
        var record = IdempotencyRecord.Reserve(IdempotencyKey, IdempotencyRecord.Checkout, UserId);
        _idempotencyKeys.FindAsync(IdempotencyKey, IdempotencyRecord.Checkout, Arg.Any<CancellationToken>())
            .Returns(record);

        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => _billing.StartCheckoutAsync(UserId, "yearly", IdempotencyKey));
    }

    [Fact]
    public async Task StartCheckoutAsync_IdempotencyKeyUsedByAnotherUser_Throws()
    {
        var record = IdempotencyRecord.Reserve(IdempotencyKey, IdempotencyRecord.Checkout, Guid.NewGuid());
        record.CompleteCheckout("cus_123", "sub_456", "secret_abc", "incomplete");
        _idempotencyKeys.FindAsync(IdempotencyKey, IdempotencyRecord.Checkout, Arg.Any<CancellationToken>())
            .Returns(record);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _billing.StartCheckoutAsync(UserId, "yearly", IdempotencyKey));
    }

    [Fact]
    public async Task StartPaymentMethodSetupAsync_AttachesCustomerId()
    {
        var user = User.Create("a@b.c", "hash");
        var sub = Subscription.StartTrial(UserId, 7);
        _users.FindByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);
        _payments.CreateSetupIntentAsync(UserId, "a@b.c", Arg.Any<CancellationToken>())
            .Returns(new SetupIntentResult("stripe", "cus_123", "seti_secret_abc"));

        var intent = await _billing.StartPaymentMethodSetupAsync(UserId);

        Assert.Equal("seti_secret_abc", intent!.ClientSecret);
        Assert.Equal("cus_123", sub.StripeCustomerId);
    }

    [Fact]
    public async Task RefundAsync_NoSubscription_Throws()
    {
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((Subscription?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _billing.RefundAsync(UserId, null, IdempotencyKey));
    }

    [Fact]
    public async Task RefundAsync_NoStripeSubscriptionId_Throws()
    {
        var sub = Subscription.StartTrial(UserId, 7);
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _billing.RefundAsync(UserId, null, IdempotencyKey));
    }

    [Fact]
    public async Task RefundAsync_DelegatesToPaymentGateway()
    {
        var sub = Subscription.StartTrial(UserId, 7);
        sub.AttachProcessor("stripe", "cus_123", "sub_456");
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);
        _payments.RefundSubscriptionAsync("sub_456", 500, IdempotencyKey, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("re_1", "succeeded", 500));

        var result = await _billing.RefundAsync(UserId, 500, IdempotencyKey);

        Assert.Equal("re_1", result!.RefundId);
        Assert.Equal(500, result.AmountCents);
    }

    [Fact]
    public async Task RefundAsync_PassesSubscriptionsLastPaymentReferenceToGateway()
    {
        var sub = Subscription.StartTrial(UserId, 7);
        sub.AttachProcessor("stripe", "cus_123", "sub_456");
        sub.RecordLastPaymentReference("psp_789");
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);
        _payments.RefundSubscriptionAsync("sub_456", 500, IdempotencyKey, "psp_789", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("re_1", "succeeded", 500));

        var result = await _billing.RefundAsync(UserId, 500, IdempotencyKey);

        Assert.Equal("re_1", result!.RefundId);
    }

    [Fact]
    public async Task RefundAsync_CompletedIdempotencyKey_ReplaysWithoutCallingGateway()
    {
        var record = IdempotencyRecord.Reserve(IdempotencyKey, IdempotencyRecord.Refund, UserId);
        record.CompleteRefund("re_1", "succeeded", 500);
        _idempotencyKeys.FindAsync(IdempotencyKey, IdempotencyRecord.Refund, Arg.Any<CancellationToken>())
            .Returns(record);

        var result = await _billing.RefundAsync(UserId, 500, IdempotencyKey);

        Assert.Equal("re_1", result!.RefundId);
        await _payments.DidNotReceive().RefundSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyEventAsync_PaymentMethodAttached_SetsDefaultOnGateway()
    {
        var handled = await _billing.ApplyEventAsync(new PaymentEvent(
            "payment_method_attached", null, "cus_123", null, null, null, null, "pm_456"));

        Assert.True(handled);
        await _payments.Received(1).SetDefaultPaymentMethodAsync(
            "cus_123", "pm_456", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyEventAsync_PaymentCaptured_RecordsLastPaymentReference()
    {
        var sub = Subscription.StartTrial(UserId, 7);
        sub.AttachProcessor("adyen", "cus_123", "ref_456");
        _subscriptions.FindByStripeCustomerIdAsync("cus_123", Arg.Any<CancellationToken>()).Returns(sub);

        var handled = await _billing.ApplyEventAsync(new PaymentEvent(
            "payment_captured", null, "cus_123", null, null, null, null,
            Processor: "adyen", PaymentReference: "psp_789"));

        Assert.True(handled);
        Assert.Equal("psp_789", sub.LastPaymentReference);
        await _subscriptions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyEventAsync_PaymentCaptured_UnknownCustomer_ReturnsFalse()
    {
        _subscriptions.FindByStripeCustomerIdAsync("cus_unknown", Arg.Any<CancellationToken>())
            .Returns((Subscription?)null);

        var handled = await _billing.ApplyEventAsync(new PaymentEvent(
            "payment_captured", null, "cus_unknown", null, null, null, null,
            Processor: "adyen", PaymentReference: "psp_789"));

        Assert.False(handled);
    }

    [Fact]
    public async Task ApplyEventAsync_DuplicateEventId_SkipsReprocessingWithoutCallingGateway()
    {
        _processedWebhookEvents.ExistsAsync("evt_dup", Arg.Any<CancellationToken>()).Returns(true);

        var handled = await _billing.ApplyEventAsync(new PaymentEvent(
            "payment_method_attached", null, "cus_123", null, null, null, null, "pm_456", "evt_dup"));

        Assert.True(handled);
        await _payments.DidNotReceive().SetDefaultPaymentMethodAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _processedWebhookEvents.DidNotReceive().SaveAsync(Arg.Any<ProcessedWebhookEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyEventAsync_NewEventId_RecordsItAfterHandling()
    {
        var handled = await _billing.ApplyEventAsync(new PaymentEvent(
            "payment_method_attached", null, "cus_123", null, null, null, null, "pm_456", "evt_new"));

        Assert.True(handled);
        await _processedWebhookEvents.Received(1).SaveAsync(
            Arg.Is<ProcessedWebhookEvent>(e => e.EventId == "evt_new"), Arg.Any<CancellationToken>());
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
