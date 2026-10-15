namespace OMyFish.ShopService.Domain.ValueObjects;

// Captured from Stripe Checkout's shipping-address collection once payment completes — not
// known at order-creation time, only once the buyer has filled it in on Stripe's hosted page.
public sealed record ShippingAddress(
    string? RecipientName, string? Line1, string? Line2, string? City, string? State,
    string? PostalCode, string? Country);
