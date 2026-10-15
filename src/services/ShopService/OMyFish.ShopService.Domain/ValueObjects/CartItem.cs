namespace OMyFish.ShopService.Domain.ValueObjects;

public sealed record CartItem(Guid ProductId, int Quantity);
