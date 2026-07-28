using OMyFish.IdentityService.Application.Interfaces;

namespace OMyFish.IdentityService.Infrastructure.Payments;

public class StripePaymentGateway : IPaymentGateway
{
    private readonly string _secretKey;
    private readonly string _webhookSecret;
    private readonly IReadOnlyDictionary<string, string> _prices;
    private readonly string _appBaseUrl;

    public StripePaymentGateway(
        string secretKey, string webhookSecret,
        IReadOnlyDictionary<string, string> prices, string appBaseUrl)
    {
        _secretKey = secretKey;
        _webhookSecret = webhookSecret;
        _prices = prices;
        _appBaseUrl = appBaseUrl;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_secretKey);

    public async Task<string?> CreateCheckoutUrlAsync(
        Guid userId, string email, string plan, CancellationToken ct = default)
    {
        if (!_prices.TryGetValue(plan, out var priceId) || string.IsNullOrEmpty(priceId))
            return null;
        if (string.IsNullOrEmpty(_secretKey))
            return null;

        var options = new Stripe.Checkout.SessionCreateOptions
        {
            Mode = "subscription",
            CustomerEmail = email,
            ClientReferenceId = userId.ToString(),
            LineItems = [new() { Price = priceId, Quantity = 1 }],
            SuccessUrl = $"{_appBaseUrl}/account?billing=success",
            CancelUrl = $"{_appBaseUrl}/account?billing=canceled",
            Metadata = new() { ["user_id"] = userId.ToString(), ["plan"] = plan },
        };
        var session = await new Stripe.Checkout.SessionService(
            new Stripe.StripeClient(_secretKey)).CreateAsync(options, cancellationToken: ct);
        return session.Url;
    }

    public PaymentEvent? VerifyWebhook(string payload, string signature)
    {
        if (string.IsNullOrEmpty(_webhookSecret)) return null;

        Stripe.Event stripeEvent;
        try
        {
            stripeEvent = Stripe.EventUtility.ConstructEvent(payload, signature, _webhookSecret);
        }
        catch
        {
            return null;
        }

        return stripeEvent.Type switch
        {
            "checkout.session.completed" => FromCheckoutCompleted(
                (Stripe.Checkout.Session)stripeEvent.Data.Object),
            "customer.subscription.updated" => FromSubscriptionEvent(
                "subscription_updated", (Stripe.Subscription)stripeEvent.Data.Object),
            "customer.subscription.deleted" => FromSubscriptionEvent(
                "subscription_deleted", (Stripe.Subscription)stripeEvent.Data.Object),
            _ => new PaymentEvent(stripeEvent.Type, null, null, null, null, null, null),
        };
    }

    private static PaymentEvent FromCheckoutCompleted(Stripe.Checkout.Session session) =>
        new("checkout_completed", session.ClientReferenceId, session.CustomerId,
            session.SubscriptionId, session.Metadata?.GetValueOrDefault("plan"), null, null);

    private static PaymentEvent FromSubscriptionEvent(string type, Stripe.Subscription sub) =>
        new(type, null, sub.CustomerId, sub.Id, null, sub.Status,
            sub.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd);
}
