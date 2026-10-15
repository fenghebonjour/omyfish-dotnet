namespace OMyFish.ShopService.Domain.Exceptions;

public sealed class OrderNotFoundException(Guid id) : Exception($"Order not found: {id}");
