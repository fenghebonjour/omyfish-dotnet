namespace OMyFish.ShopService.Domain.Exceptions;

public sealed class ProductNotFoundException(Guid id) : Exception($"Product not found: {id}");
