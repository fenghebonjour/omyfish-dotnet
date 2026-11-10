using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Infrastructure.Payments;
using Polly.CircuitBreaker;
using Xunit;

namespace OMyFish.ShopService.Tests;

public class ResilientPaymentGatewayTests
{
    private static readonly PaymentLineItem[] Lines = [new("Lure", 1000, 1)];

    private static Task<CheckoutSession?> Create(IPaymentGateway g) =>
        g.CreateCheckoutSessionAsync(Guid.NewGuid(), Lines, "ok", "cancel", "idem", null);

    [Fact]
    public async Task TransientFailure_IsRetriedWithTheSameIdempotencyKey()
    {
        var inner = Substitute.For<IPaymentGateway>();
        inner.CreateCheckoutSessionAsync(default, default!, default!, default!, default!, default)
            .ReturnsForAnyArgs(
                _ => throw new InvalidOperationException("stripe blip"),
                _ => Task.FromResult<CheckoutSession?>(new CheckoutSession("sess_1", "https://stripe.test/1")));

        var session = await Create(new ResilientPaymentGateway(inner));

        Assert.Equal("sess_1", session!.SessionId);
        await inner.Received(2).CreateCheckoutSessionAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<PaymentLineItem>>(), "ok", "cancel", "idem", null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SustainedFailures_OpenTheCircuitAndStopCallingTheProcessor()
    {
        var inner = Substitute.For<IPaymentGateway>();
        inner.CreateCheckoutSessionAsync(default, default!, default!, default!, default!, default)
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("stripe down"));
        var gateway = new ResilientPaymentGateway(inner);

        // 2 calls x 3 attempts = 6 failures, past the 5-call minimum at a 100% failure ratio.
        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAnyAsync<Exception>(() => Create(gateway));
        inner.ClearReceivedCalls();

        await Assert.ThrowsAsync<BrokenCircuitException>(() => Create(gateway));
        await inner.DidNotReceiveWithAnyArgs().CreateCheckoutSessionAsync(default, default!, default!, default!, default!, default);
    }
}
