using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.ValueObjects;

namespace OMyFish.ShopService.Application.Dtos;

public static class ShopMappingExtensions
{
    public static ProductDto ToDto(this Product p) =>
        new(p.Id, p.Name, p.Description, p.PriceCents, p.ImageUrl, p.StockQuantity);

    public static CartDto ToDto(this Cart c) =>
        new(c.Id, c.Items.Select(i => new CartItemDto(i.ProductId, i.Quantity)).ToList());

    public static OrderDto ToDto(this Order o) => new(
        o.Id, o.Status, o.TotalCents,
        o.Lines.Select(l => new OrderLineDto(l.ProductId, l.ProductName, l.UnitPriceCents, l.Quantity)).ToList(),
        o.CreatedAt, o.PaidAt, o.ReturnRequestedAt, o.ReturnedAt, o.ReturnReason,
        o.ShippingAddress?.ToDto());

    private static ShippingAddressDto ToDto(this ShippingAddress a) =>
        new(a.RecipientName, a.Line1, a.Line2, a.City, a.State, a.PostalCode, a.Country);
}
