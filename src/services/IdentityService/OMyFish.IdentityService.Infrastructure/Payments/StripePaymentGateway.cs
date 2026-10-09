using OMyFish.IdentityService.Application.Interfaces;

namespace OMyFish.IdentityService.Infrastructure.Payments;

public class StripePaymentGateway : IPaymentGateway
{
    private readonly string _secretKey;
    private readonly string _webhookSecret;
    private readonly IReadOnlyDictionary<string, string> _prices;

    public StripePaymentGateway(
        string secretKey, string webhookSecret, IReadOnlyDictionary<string, string> prices)
    {
        _secretKey = secretKey;
        _webhookSecret = webhookSecret;
        _prices = prices;
    }

    public string Name => "stripe";

    public bool IsConfigured => !string.IsNullOrEmpty(_secretKey);

    private Stripe.StripeClient Client => new(_secretKey);

    public async Task<SubscriptionIntentResult?> CreateSubscriptionIntentAsync(
        Guid userId, string email, string plan, string idempotencyKey, CancellationToken ct = default)
    {
        if (!_prices.TryGetValue(plan, out var priceId) || string.IsNullOrEmpty(priceId))
            return null;
        if (string.IsNullOrEmpty(_secretKey))
            return null;

        var client = Client;
        var customerId = await FindOrCreateCustomerAsync(client, userId, email, ct);

        var options = new Stripe.SubscriptionCreateOptions
        {
            Customer = customerId,
            Items = [new Stripe.SubscriptionItemOptions { Price = priceId }],
            PaymentBehavior = "default_incomplete",
            Expand = ["latest_invoice.confirmation_secret"],
            Metadata = new() { ["user_id"] = userId.ToString(), ["plan"] = plan },
        };
        var requestOptions = new Stripe.RequestOptions { IdempotencyKey = idempotencyKey };
        var subscription = await new Stripe.SubscriptionService(client).CreateAsync(options, requestOptions, ct);

        var clientSecret = subscription.LatestInvoice?.ConfirmationSecret?.ClientSecret;
        return new SubscriptionIntentResult(
            Name, subscription.CustomerId, subscription.Id, clientSecret, subscription.Status);
    }

    public async Task<SetupIntentResult?> CreateSetupIntentAsync(
        Guid userId, string email, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_secretKey)) return null;

        var client = Client;
        var customerId = await FindOrCreateCustomerAsync(client, userId, email, ct);

        var setupIntent = await new Stripe.SetupIntentService(client).CreateAsync(
            new Stripe.SetupIntentCreateOptions { Customer = customerId, Usage = "off_session" },
            cancellationToken: ct);

        return new SetupIntentResult(Name, customerId, setupIntent.ClientSecret);
    }

    public async Task SetDefaultPaymentMethodAsync(
        string customerId, string paymentMethodId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_secretKey)) return;

        await new Stripe.CustomerService(Client).UpdateAsync(customerId, new Stripe.CustomerUpdateOptions
        {
            InvoiceSettings = new Stripe.CustomerInvoiceSettingsOptions
            {
                DefaultPaymentMethod = paymentMethodId,
            },
        }, cancellationToken: ct);
    }

    public async Task<RefundResult?> RefundSubscriptionAsync(
        string subscriptionId, long? amountCents, string idempotencyKey,
        string? lastPaymentReference = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_secretKey) || string.IsNullOrEmpty(subscriptionId)) return null;

        var client = Client;
        var subscription = await new Stripe.SubscriptionService(client).GetAsync(subscriptionId, cancellationToken: ct);
        var invoiceId = subscription.LatestInvoiceId;
        if (string.IsNullOrEmpty(invoiceId)) return null;

        var payments = await new Stripe.InvoicePaymentService(client).ListAsync(
            new Stripe.InvoicePaymentListOptions { Invoice = invoiceId, Status = "paid", Limit = 1 },
            cancellationToken: ct);
        var paymentIntentId = payments.Data.FirstOrDefault()?.Payment?.PaymentIntentId;
        if (string.IsNullOrEmpty(paymentIntentId)) return null;

        var refundOptions = new Stripe.RefundCreateOptions { PaymentIntent = paymentIntentId };
        if (amountCents is not null) refundOptions.Amount = amountCents;
        var requestOptions = new Stripe.RequestOptions { IdempotencyKey = idempotencyKey };
        var refund = await new Stripe.RefundService(client).CreateAsync(refundOptions, requestOptions, ct);

        return new RefundResult(refund.Id, refund.Status, amountCents);
    }

    public Task<PaymentEvent?> VerifyWebhookAsync(
        string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_webhookSecret)) return Task.FromResult<PaymentEvent?>(null);
        var signature = headers.GetValueOrDefault("stripe-signature") ?? "";

        Stripe.Event stripeEvent;
        try
        {
            stripeEvent = Stripe.EventUtility.ConstructEvent(payload, signature, _webhookSecret);
        }
        catch
        {
            return Task.FromResult<PaymentEvent?>(null);
        }

        PaymentEvent? result = stripeEvent.Type switch
        {
            "customer.subscription.updated" => FromSubscriptionEvent(
                "subscription_updated", (Stripe.Subscription)stripeEvent.Data.Object, stripeEvent.Id),
            "customer.subscription.deleted" => FromSubscriptionEvent(
                "subscription_deleted", (Stripe.Subscription)stripeEvent.Data.Object, stripeEvent.Id),
            "setup_intent.succeeded" => FromSetupIntent(
                (Stripe.SetupIntent)stripeEvent.Data.Object, stripeEvent.Id),
            "invoice.payment_failed" => FromInvoicePaymentFailed(
                (Stripe.Invoice)stripeEvent.Data.Object, stripeEvent.Id),
            _ => null,
        };
        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<ReconciliationCandidate>> ListRecentSubscriptionsAsync(
        DateTime since, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_secretKey)) return [];

        var client = Client;
        var options = new Stripe.SubscriptionListOptions
        {
            Created = new Stripe.DateRangeOptions { GreaterThanOrEqual = since },
            Limit = 100,
        };
        var candidates = new List<ReconciliationCandidate>();
        await foreach (var sub in new Stripe.SubscriptionService(client).ListAutoPagingAsync(options, cancellationToken: ct))
        {
            var userIdStr = sub.Metadata?.GetValueOrDefault("user_id");
            Guid? userId = Guid.TryParse(userIdStr, out var parsed) ? parsed : null;
            var periodEnd = sub.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd;
            candidates.Add(new ReconciliationCandidate(
                Name, userId, sub.CustomerId, sub.Id, sub.Metadata?.GetValueOrDefault("plan"), sub.Status, periodEnd));
        }
        return candidates;
    }

    public async Task<string?> CreatePortalSessionAsync(
        string customerId, string returnUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_secretKey)) return null;

        var session = await new Stripe.BillingPortal.SessionService(Client).CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions { Customer = customerId, ReturnUrl = returnUrl },
            cancellationToken: ct);
        return session.Url;
    }

    private PaymentEvent FromSubscriptionEvent(string type, Stripe.Subscription sub, string eventId) =>
        new(type, null, sub.CustomerId, sub.Id, PlanForPriceId(sub.Items?.Data?.FirstOrDefault()?.Price?.Id),
            sub.Status, sub.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd, EventId: eventId, Processor: "stripe");

    private static PaymentEvent FromSetupIntent(Stripe.SetupIntent setupIntent, string eventId) =>
        new("payment_method_attached", null, setupIntent.CustomerId, null, null, null, null,
            setupIntent.PaymentMethodId, eventId, Processor: "stripe");

    private static PaymentEvent? FromInvoicePaymentFailed(Stripe.Invoice invoice, string eventId)
    {
        var subscriptionId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
        if (string.IsNullOrEmpty(subscriptionId)) return null;
        return new PaymentEvent(
            "payment_failed", null, invoice.CustomerId, subscriptionId, null, null, null,
            EventId: eventId, Processor: "stripe");
    }

    /// Reverses _prices to recover our plan name from a Stripe price id on a webhook event.
    private string? PlanForPriceId(string? priceId)
    {
        if (priceId is null) return null;
        return _prices.FirstOrDefault(kv => kv.Value == priceId).Key;
    }

    private static async Task<string> FindOrCreateCustomerAsync(
        Stripe.StripeClient client, Guid userId, string email, CancellationToken ct)
    {
        var existing = await new Stripe.CustomerService(client).ListAsync(
            new Stripe.CustomerListOptions { Email = email, Limit = 1 }, cancellationToken: ct);
        var found = existing.Data.FirstOrDefault();
        if (found is not null) return found.Id;

        var created = await new Stripe.CustomerService(client).CreateAsync(new Stripe.CustomerCreateOptions
        {
            Email = email,
            Metadata = new() { ["user_id"] = userId.ToString() },
        }, cancellationToken: ct);
        return created.Id;
    }
}
