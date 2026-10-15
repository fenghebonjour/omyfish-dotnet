namespace OMyFish.ShopService.Domain.Exceptions;

public sealed class CartEmptyException(Guid? userId, Guid? guestToken)
    : Exception(userId is not null ? $"Cart is empty for user: {userId}" : $"Cart is empty for guest: {guestToken}");
