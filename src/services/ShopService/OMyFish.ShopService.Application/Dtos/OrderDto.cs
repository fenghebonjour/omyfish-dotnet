using OMyFish.ShopService.Domain.Entities;

namespace OMyFish.ShopService.Application.Dtos;

public sealed record OrderDto(
    Guid Id, OrderStatus Status, long TotalCents, IReadOnlyList<OrderLineDto> Lines,
    DateTime CreatedAt, DateTime? PaidAt, DateTime? ReturnRequestedAt, DateTime? ReturnedAt,
    string? ReturnReason, ShippingAddressDto? ShippingAddress);

public sealed record OrderLineDto(Guid ProductId, string ProductName, long UnitPriceCents, int Quantity);

public sealed record ShippingAddressDto(
    string? RecipientName, string? Line1, string? Line2, string? City, string? State,
    string? PostalCode, string? Country);
