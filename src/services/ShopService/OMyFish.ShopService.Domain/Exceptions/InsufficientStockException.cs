namespace OMyFish.ShopService.Domain.Exceptions;

public sealed class InsufficientStockException(Guid productId, int requested, int available)
    : Exception($"Insufficient stock for product {productId}: requested {requested}, available {available}");
