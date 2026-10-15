using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.ShopService.Domain.ValueObjects;
using OMyFish.Shared.BuildingBlocks.Domain;

namespace OMyFish.ShopService.Domain.Entities;

public sealed class Cart : AggregateRoot<Guid>
{
    private readonly List<CartItem> _items = [];

    private Cart(Guid id) : base(id) { }

    public Guid? UserId { get; private set; }
    public Guid? GuestToken { get; private set; }
    public IReadOnlyList<CartItem> Items => _items.AsReadOnly();
    public bool IsGuest => GuestToken is not null;

    public static Cart CreateEmpty(CartOwner owner)
    {
        if ((owner.UserId is null) == (owner.GuestToken is null))
            throw new ArgumentException("Cart requires exactly one of UserId or GuestToken");
        return new Cart(Guid.NewGuid()) { UserId = owner.UserId, GuestToken = owner.GuestToken };
    }

    public void AddItem(Guid productId, int quantity)
    {
        var existing = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is not null)
            SetQuantity(productId, existing.Quantity + quantity);
        else
            _items.Add(new CartItem(productId, quantity));
    }

    public void SetQuantity(Guid productId, int quantity)
    {
        _items.RemoveAll(i => i.ProductId == productId);
        if (quantity > 0)
            _items.Add(new CartItem(productId, quantity));
    }

    public void Clear() => _items.Clear();

    // Folds another cart's items (e.g. a guest cart being merged on login) into this one,
    // summing quantities for products already present.
    public void MergeFrom(Cart other)
    {
        foreach (var item in other.Items)
            AddItem(item.ProductId, item.Quantity);
    }

    public void RequireNotEmpty()
    {
        if (_items.Count == 0)
            throw new CartEmptyException(UserId, GuestToken);
    }
}
