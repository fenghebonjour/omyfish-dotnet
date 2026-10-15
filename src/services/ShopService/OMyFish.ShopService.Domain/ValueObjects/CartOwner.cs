namespace OMyFish.ShopService.Domain.ValueObjects;

// Either an authenticated user's id or an anonymous guest's cart token, never both — the two
// identity schemes feeding cart/checkout now that guest checkout exists alongside account
// checkout.
public sealed record CartOwner
{
    public Guid? UserId { get; }
    public Guid? GuestToken { get; }

    private CartOwner(Guid? userId, Guid? guestToken)
    {
        if ((userId is null) == (guestToken is null))
            throw new ArgumentException("CartOwner requires exactly one of UserId or GuestToken");
        UserId = userId;
        GuestToken = guestToken;
    }

    public static CartOwner OfUser(Guid userId) => new(userId, null);
    public static CartOwner OfGuest(Guid guestToken) => new(null, guestToken);

    public bool IsGuest => GuestToken is not null;
}
