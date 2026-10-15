using Microsoft.EntityFrameworkCore;
using OMyFish.ShopService.Domain.Entities;

namespace OMyFish.ShopService.Infrastructure.Persistence;

public class ShopDbContext : DbContext
{
    public ShopDbContext(DbContextOptions<ShopDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(p =>
        {
            p.ToTable("products", "shop");
            p.HasKey(x => x.Id);
            p.Property(x => x.Id).HasColumnName("id");
            p.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            p.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
            p.Property(x => x.PriceCents).HasColumnName("price_cents");
            p.Property(x => x.ImageUrl).HasColumnName("image_url");
            p.Property(x => x.ImageStorageKey).HasColumnName("image_storage_key").HasMaxLength(512);
            p.Property(x => x.StockQuantity).HasColumnName("stock_quantity");
            p.Property(x => x.Active).HasColumnName("active");
            p.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Cart>(c =>
        {
            c.ToTable("carts", "shop");
            c.HasKey(x => x.Id);
            c.Property(x => x.Id).HasColumnName("id");
            c.Property(x => x.UserId).HasColumnName("user_id");
            c.Property(x => x.GuestToken).HasColumnName("guest_token");

            // Items is a read-only (IReadOnlyList) navigation backed by the private _items
            // field — EF Core's documented "backing field" convention binds straight to it,
            // the .NET equivalent of Java's @ElementCollection(fetch = EAGER).
            c.OwnsMany(x => x.Items, items =>
            {
                items.ToTable("cart_items", "shop");
                items.WithOwner().HasForeignKey("cart_id");
                items.Property(x => x.ProductId).HasColumnName("product_id");
                items.Property(x => x.Quantity).HasColumnName("quantity");
                items.HasKey("cart_id", nameof(Domain.ValueObjects.CartItem.ProductId));
            });
            c.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Order>(o =>
        {
            o.ToTable("orders", "shop");
            o.HasKey(x => x.Id);
            o.Property(x => x.Id).HasColumnName("id");
            o.Property(x => x.UserId).HasColumnName("user_id");
            o.Property(x => x.GuestEmail).HasColumnName("guest_email").HasMaxLength(255);
            o.Property(x => x.TotalCents).HasColumnName("total_cents");
            o.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
            o.Property(x => x.StripeSessionId).HasColumnName("stripe_session_id");
            o.Property(x => x.StripePaymentIntentId).HasColumnName("stripe_payment_intent_id");
            o.Property(x => x.CreatedAt).HasColumnName("created_at");
            o.Property(x => x.PaidAt).HasColumnName("paid_at");
            o.Property(x => x.ReturnRequestedAt).HasColumnName("return_requested_at");
            o.Property(x => x.ReturnedAt).HasColumnName("returned_at");
            o.Property(x => x.ReturnReason).HasColumnName("return_reason").HasMaxLength(2000);

            o.OwnsMany(x => x.Lines, lines =>
            {
                lines.ToTable("order_lines", "shop");
                lines.WithOwner().HasForeignKey("order_id");
                lines.Property(x => x.ProductId).HasColumnName("product_id");
                lines.Property(x => x.ProductName).HasColumnName("product_name").HasMaxLength(255);
                lines.Property(x => x.UnitPriceCents).HasColumnName("unit_price_cents");
                lines.Property(x => x.Quantity).HasColumnName("quantity");
                // One line per product per order (ReserveOrderCommandHandler creates exactly one
                // OrderLine per distinct cart item), so FK + ProductId is a natural composite key
                // — no separate ordinal/line-index shadow property needed.
                lines.HasKey("order_id", nameof(Domain.ValueObjects.OrderLine.ProductId));
            });

            // Flat columns rather than a required owned type: an all-null set of columns makes
            // EF Core materialize ShippingAddress as null, giving the same unambiguous
            // "no address yet" marker Java's flat shipping_* columns document.
            o.OwnsOne(x => x.ShippingAddress, sa =>
            {
                sa.Property(x => x.RecipientName).HasColumnName("shipping_recipient_name");
                sa.Property(x => x.Line1).HasColumnName("shipping_line1");
                sa.Property(x => x.Line2).HasColumnName("shipping_line2");
                sa.Property(x => x.City).HasColumnName("shipping_city");
                sa.Property(x => x.State).HasColumnName("shipping_state");
                sa.Property(x => x.PostalCode).HasColumnName("shipping_postal_code");
                sa.Property(x => x.Country).HasColumnName("shipping_country").HasMaxLength(2);
            });
            o.Ignore(x => x.DomainEvents);
        });
    }
}
