using Microsoft.Extensions.Logging;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.ValueObjects;

namespace OMyFish.ShopService.Infrastructure.Payments;

public class StripeCheckoutGateway : IPaymentGateway
{
    private readonly string _secretKey;
    private readonly string _webhookSecret;
    private readonly string _currency;
    private readonly ILogger<StripeCheckoutGateway> _logger;

    public StripeCheckoutGateway(
        string secretKey, string webhookSecret, string currency, ILogger<StripeCheckoutGateway> logger)
    {
        _secretKey = secretKey;
        _webhookSecret = webhookSecret;
        _currency = currency;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_secretKey);

    private Stripe.StripeClient Client => new(_secretKey);

    public async Task<CheckoutSession?> CreateCheckoutSessionAsync(
        Guid orderId, IReadOnlyList<PaymentLineItem> lines, string successUrl, string cancelUrl,
        string idempotencyKey, string? customerEmail, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_secretKey)) return null;

        var options = new Stripe.Checkout.SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = new Dictionary<string, string> { ["order_id"] = orderId.ToString() },
            // Lures/bait are physical goods — every checkout must collect somewhere to ship
            // them. Canada-only for now, matching the rest of the app's Quebec/CAD focus.
            ShippingAddressCollection = new Stripe.Checkout.SessionShippingAddressCollectionOptions
            {
                AllowedCountries = ["CA"]
            },
            LineItems = lines.Select(l => new Stripe.Checkout.SessionLineItemOptions
            {
                Quantity = l.Quantity,
                PriceData = new Stripe.Checkout.SessionLineItemPriceDataOptions
                {
                    Currency = _currency,
                    UnitAmount = l.UnitPriceCents,
                    ProductData = new Stripe.Checkout.SessionLineItemPriceDataProductDataOptions
                    {
                        Name = l.Name
                    }
                }
            }).ToList()
        };

        // Only set for guest checkout — an account checkout has no reliable email handy at this
        // layer, and Stripe's own page still asks for one either way.
        if (!string.IsNullOrWhiteSpace(customerEmail))
            options.CustomerEmail = customerEmail;

        var requestOptions = new Stripe.RequestOptions { ApiKey = _secretKey, IdempotencyKey = idempotencyKey };
        try
        {
            var session = await new Stripe.Checkout.SessionService(Client).CreateAsync(options, requestOptions, ct);
            return new CheckoutSession(session.Id, session.Url);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Stripe checkout session creation failed: {e.Message}", e);
        }
    }

    public async Task<RefundResult?> RefundAsync(
        string? paymentIntentId, long? amountCents, string idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_secretKey) || string.IsNullOrEmpty(paymentIntentId)) return null;

        var options = new Stripe.RefundCreateOptions { PaymentIntent = paymentIntentId };
        if (amountCents is not null) options.Amount = amountCents;

        var requestOptions = new Stripe.RequestOptions { ApiKey = _secretKey, IdempotencyKey = idempotencyKey };
        try
        {
            var refund = await new Stripe.RefundService(Client).CreateAsync(options, requestOptions, ct);
            return new RefundResult(refund.Id, refund.Status, amountCents);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Stripe refund failed: {e.Message}", e);
        }
    }

    public Task<PaymentEvent?> VerifyWebhookAsync(
        string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_webhookSecret)) return Task.FromResult<PaymentEvent?>(null);
        var signature = headers.GetValueOrDefault("stripe-signature") ?? "";

        Stripe.Event stripeEvent;
        try
        {
            // throwOnApiVersionMismatch: false — Stripe.net's default throws when the event's
            // api_version doesn't match the version Stripe.net was compiled for, which is common
            // in a sandbox whose account api_version trails the library (the same bug Java's
            // StripeCheckoutAdapter and this repo's own IdentityService.StripePaymentGateway
            // both carry a fix for — see BACKLOG item L).
            stripeEvent = Stripe.EventUtility.ConstructEvent(payload, signature, _webhookSecret, throwOnApiVersionMismatch: false);
        }
        catch
        {
            return Task.FromResult<PaymentEvent?>(null);
        }

        if (stripeEvent.ApiVersion != Stripe.StripeConfiguration.ApiVersion)
        {
            _logger.LogWarning(
                "Stripe webhook {EventId} ({EventType}): event api_version {EventApiVersion} does not match " +
                "Stripe.net's compiled version {SdkApiVersion}; deserialized with throwOnApiVersionMismatch: false",
                stripeEvent.Id, stripeEvent.Type, stripeEvent.ApiVersion, Stripe.StripeConfiguration.ApiVersion);
        }

        PaymentEvent? result = stripeEvent.Type switch
        {
            "checkout.session.completed" => FromSession(
                PaymentEventType.SessionCompleted, (Stripe.Checkout.Session)stripeEvent.Data.Object),
            "checkout.session.expired" => FromSession(
                PaymentEventType.SessionExpired, (Stripe.Checkout.Session)stripeEvent.Data.Object),
            _ => null,
        };
        return Task.FromResult(result);
    }

    private static PaymentEvent FromSession(PaymentEventType type, Stripe.Checkout.Session session) =>
        new(type, session.Id, session.PaymentIntentId,
            type == PaymentEventType.SessionCompleted ? ExtractShippingAddress(session) : null);

    // Null whenever the buyer hasn't actually filled in the shipping step yet — shouldn't
    // happen for a genuinely completed session (shipping-address collection is always set on
    // creation), but a missing address shouldn't crash the webhook if it ever does.
    private static ShippingAddress? ExtractShippingAddress(Stripe.Checkout.Session session)
    {
        var details = session.CollectedInformation?.ShippingDetails;
        var address = details?.Address;
        if (address is null) return null;

        return new ShippingAddress(
            details!.Name, address.Line1, address.Line2, address.City, address.State,
            address.PostalCode, address.Country);
    }
}
