namespace OMyFish.ShopService.Domain.ValueObjects;

// UnitPriceCents and ProductName are snapshotted at checkout time so a later catalog price or
// name change never alters a past order's recorded total.
public sealed record OrderLine(Guid ProductId, string ProductName, long UnitPriceCents, int Quantity)
{
    public long SubtotalCents => UnitPriceCents * Quantity;
}
