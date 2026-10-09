using NSubstitute;
using OMyFish.IdentityService.Application;
using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Domain.Interfaces;
using Xunit;

namespace OMyFish.IdentityService.Tests;

public class ReconciliationServiceTest
{
    private readonly IPaymentGateway _payments = Substitute.For<IPaymentGateway>();
    private readonly ISubscriptionRepository _subscriptions = Substitute.For<ISubscriptionRepository>();
    private readonly BillingService _billing = Substitute.For<BillingService>(
        Substitute.For<ISubscriptionRepository>(), Substitute.For<IUserRepository>(),
        new PaymentProcessorRegistry([Substitute.For<IPaymentGateway>()], "stripe"),
        Substitute.For<IIdempotencyKeyRepository>(), Substitute.For<IProcessedWebhookEventRepository>());
    private readonly ReconciliationService _service;

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Since = DateTime.UtcNow.AddHours(-1);

    public ReconciliationServiceTest()
    {
        _payments.Name.Returns("stripe");
        _payments.IsConfigured.Returns(true);
        var processors = new PaymentProcessorRegistry([_payments], "stripe");
        _service = new ReconciliationService(processors, _subscriptions, _billing);
    }

    [Fact]
    public async Task MismatchedLink_GetsRepairedAndStatusResynced()
    {
        var sub = Subscription.StartTrial(UserId, 7);
        var periodEnd = DateTime.UtcNow.AddDays(1);
        _payments.ListRecentSubscriptionsAsync(Since, Arg.Any<CancellationToken>()).Returns(
            new List<ReconciliationCandidate> { new("stripe", UserId, "cus_123", "sub_456", "monthly", "active", periodEnd) });
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);

        var result = await _service.ReconcileAsync(Since);

        Assert.Equal(1, result.Checked);
        Assert.Single(result.Repaired);
        Assert.Empty(result.Errors);
        Assert.Equal("cus_123", sub.StripeCustomerId);
        Assert.Equal("sub_456", sub.StripeSubscriptionId);
        await _billing.Received(1).ApplyEventAsync(
            Arg.Is<PaymentEvent>(e => e.Type == "subscription_updated" && e.CustomerId == "cus_123"
                && e.SubscriptionId == "sub_456" && e.ProviderStatus == "active" && e.Plan == "monthly"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyLinked_SkipsRepairButStillResyncsStatus()
    {
        var sub = Subscription.StartTrial(UserId, 7);
        sub.AttachProcessor("stripe", "cus_123", "sub_456");
        _payments.ListRecentSubscriptionsAsync(Since, Arg.Any<CancellationToken>()).Returns(
            new List<ReconciliationCandidate> { new("stripe", UserId, "cus_123", "sub_456", "monthly", "canceled", null) });
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(sub);

        var result = await _service.ReconcileAsync(Since);

        Assert.Empty(result.Repaired);
        await _subscriptions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _billing.Received(1).ApplyEventAsync(Arg.Any<PaymentEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CandidateWithNoUserId_RecordedAsErrorAndSkipped()
    {
        _payments.ListRecentSubscriptionsAsync(Since, Arg.Any<CancellationToken>()).Returns(
            new List<ReconciliationCandidate> { new("stripe", null, "cus_999", "sub_999", "monthly", "active", null) });

        var result = await _service.ReconcileAsync(Since);

        Assert.Equal(1, result.Checked);
        Assert.Empty(result.Repaired);
        Assert.Single(result.Errors);
        await _subscriptions.DidNotReceiveWithAnyArgs().FindByUserIdAsync(default, default);
        await _billing.DidNotReceiveWithAnyArgs().ApplyEventAsync(default!, default);
    }

    [Fact]
    public async Task NoLocalSubscriptionYet_CreatesOneThenLinksIt()
    {
        _payments.ListRecentSubscriptionsAsync(Since, Arg.Any<CancellationToken>()).Returns(
            new List<ReconciliationCandidate> { new("stripe", UserId, "cus_123", "sub_456", "monthly", "active", null) });
        _subscriptions.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((Subscription?)null);

        var result = await _service.ReconcileAsync(Since);

        Assert.Single(result.Repaired);
        await _subscriptions.Received(1).CreateAsync(Arg.Any<Subscription>(), Arg.Any<CancellationToken>());
        await _subscriptions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GatewayListFails_RecordsErrorAndChecksNothing()
    {
        _payments.ListRecentSubscriptionsAsync(Since, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<ReconciliationCandidate>>(new InvalidOperationException("Stripe is down")));

        var result = await _service.ReconcileAsync(Since);

        Assert.Equal(0, result.Checked);
        Assert.Single(result.Errors);
        Assert.Contains("Stripe is down", result.Errors[0]);
    }
}
