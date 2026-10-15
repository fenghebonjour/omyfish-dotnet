namespace OMyFish.ShopService.Application.Dtos;

public sealed record ProductDto(
    Guid Id, string Name, string? Description, long PriceCents, string? ImageUrl, int StockQuantity);
