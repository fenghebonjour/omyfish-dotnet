using Microsoft.EntityFrameworkCore;
using OMyFish.IdentityService.Domain.Entities;

namespace OMyFish.IdentityService.Infrastructure.Persistence;

public class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(u =>
        {
            u.ToTable("users");
            u.HasKey(x => x.Id);
            u.Property(x => x.Id).HasColumnName("id");
            u.Property(x => x.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
            u.HasIndex(x => x.Email).IsUnique();
            u.Property(x => x.HashedPassword).HasColumnName("hashed_password").HasMaxLength(255).IsRequired();
            u.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(100);
            u.Property(x => x.Role).HasColumnName("role").HasMaxLength(50).HasDefaultValue("USER");
            u.Property(x => x.IsActive).HasColumnName("is_active");
            u.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            u.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        modelBuilder.Entity<ApiKey>(k =>
        {
            k.ToTable("api_keys");
            k.HasKey(x => x.Id);
            k.Property(x => x.Id).HasColumnName("id");
            k.Property(x => x.UserId).HasColumnName("user_id");
            k.Property(x => x.KeyHash).HasColumnName("key_hash").HasMaxLength(64).IsRequired();
            k.HasIndex(x => x.KeyHash).IsUnique();
            k.Property(x => x.Name).HasColumnName("name").HasMaxLength(100);
            k.Property(x => x.IsActive).HasColumnName("is_active");
            k.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            k.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            k.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Subscription>(s =>
        {
            s.ToTable("subscriptions");
            s.HasKey(x => x.Id);
            s.Property(x => x.Id).HasColumnName("id");
            s.Property(x => x.UserId).HasColumnName("user_id");
            s.HasIndex(x => x.UserId).IsUnique();
            s.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("trialing");
            s.Property(x => x.Plan).HasColumnName("plan").HasMaxLength(20);
            s.Property(x => x.TrialEnd).HasColumnName("trial_end");
            s.Property(x => x.CurrentPeriodEnd).HasColumnName("current_period_end");
            s.Property(x => x.StripeCustomerId).HasColumnName("stripe_customer_id").HasMaxLength(255);
            s.HasIndex(x => x.StripeCustomerId);
            s.Property(x => x.StripeSubscriptionId).HasColumnName("stripe_subscription_id").HasMaxLength(255);
            s.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            s.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            s.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
