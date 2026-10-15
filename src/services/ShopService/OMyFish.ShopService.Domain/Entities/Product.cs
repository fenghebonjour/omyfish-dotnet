using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.Shared.BuildingBlocks.Domain;

namespace OMyFish.ShopService.Domain.Entities;

public sealed class Product : AggregateRoot<Guid>
{
    private Product(Guid id) : base(id) { }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public long PriceCents { get; private set; }
    public string? ImageUrl { get; private set; }
    public string? ImageStorageKey { get; private set; }
    public int StockQuantity { get; private set; }
    public bool Active { get; private set; }

    public static Product Create(
        string name, string? description, long priceCents, string? imageUrl, int stockQuantity, bool active)
    {
        return new Product(Guid.NewGuid())
        {
            Name = name,
            Description = description,
            PriceCents = priceCents,
            ImageUrl = imageUrl,
            StockQuantity = stockQuantity,
            Active = active
        };
    }

    // Called once an admin uploads a real photo — ImageUrl now points at this service's own
    // serve-image endpoint (never at MinIO directly), ImageStorageKey at the underlying object.
    public void SetImage(string imageUrl, string imageStorageKey)
    {
        ImageUrl = imageUrl;
        ImageStorageKey = imageStorageKey;
    }

    public void DecreaseStock(int quantity)
    {
        if (quantity > StockQuantity)
            throw new InsufficientStockException(Id, quantity, StockQuantity);
        StockQuantity -= quantity;
    }

    public void Restock(int quantity) => StockQuantity += quantity;
}
