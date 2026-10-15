namespace OMyFish.ShopService.Domain.Exceptions;

public sealed class InvalidOrderStateException(Guid orderId, string reason)
    : Exception($"Order {orderId} cannot perform this action: {reason}");
