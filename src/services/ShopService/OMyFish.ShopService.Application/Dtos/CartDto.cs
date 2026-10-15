namespace OMyFish.ShopService.Application.Dtos;

public sealed record CartDto(Guid CartId, IReadOnlyList<CartItemDto> Items);

public sealed record CartItemDto(Guid ProductId, int Quantity);
