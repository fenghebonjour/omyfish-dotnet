using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using OMyFish.ShopService.Application.Interfaces;

namespace OMyFish.ShopService.Infrastructure.Payments;

/// Wraps one processor's outbound calls (create checkout session, refund) in its own Polly
/// pipeline: retry → circuit breaker → bulkhead (concurrency limit), outermost first — the same
/// nesting omyfish-java's @Retry/@CircuitBreaker/@Bulkhead annotations get from Resilience4j.
/// One pipeline per processor, not one shared, so Stripe failing doesn't also trip the breaker
/// on PayPal/Adyen. Retrying is safe because every gateway passes idempotencyKey through to the
/// processor's own idempotency mechanism (Stripe/Adyen idempotency keys, PayPal-Request-Id).
/// Webhook verification isn't wrapped: the processor itself retries an undelivered webhook.
///
/// An open circuit (BrokenCircuitException) or a full bulkhead (RateLimiterRejectedException)
/// surfaces like any other processor failure — CheckoutCommandHandler compensates and the
/// endpoint returns 503.
public sealed class ResilientPaymentGateway : IPaymentGateway
{
    private readonly IPaymentGateway _inner;
    private readonly ResiliencePipeline _pipeline;

    public ResilientPaymentGateway(IPaymentGateway inner, TimeProvider? timeProvider = null)
    {
        _inner = inner;
        _pipeline = BuildPipeline(timeProvider ?? TimeProvider.System);
    }

    public string Name => _inner.Name;
    public bool IsConfigured => _inner.IsConfigured;

    // Same numbers as omyfish-java's resilience4j "payment-gateway" config.
    private static ResiliencePipeline BuildPipeline(TimeProvider timeProvider)
    {
        var notCancellation = new PredicateBuilder().Handle<Exception>(e => e is not OperationCanceledException);
        return new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = notCancellation,
                MaxRetryAttempts = 2, // 3 attempts total
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = notCancellation,
                FailureRatio = 0.5,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30),
            })
            .AddConcurrencyLimiter(permitLimit: 20, queueLimit: 0)
            .Build();
    }

    public Task<CheckoutSession?> CreateCheckoutSessionAsync(
        Guid orderId, IReadOnlyList<PaymentLineItem> lines, string successUrl, string cancelUrl,
        string idempotencyKey, string? customerEmail, CancellationToken ct = default) =>
        _pipeline.ExecuteAsync(async token => await _inner.CreateCheckoutSessionAsync(
            orderId, lines, successUrl, cancelUrl, idempotencyKey, customerEmail, token), ct).AsTask();

    public Task<RefundResult?> RefundAsync(
        string? paymentIntentId, long? amountCents, string idempotencyKey, CancellationToken ct = default) =>
        _pipeline.ExecuteAsync(async token =>
            await _inner.RefundAsync(paymentIntentId, amountCents, idempotencyKey, token), ct).AsTask();

    public Task<PaymentEvent?> VerifyWebhookAsync(
        string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default) =>
        _inner.VerifyWebhookAsync(payload, headers, ct);
}
