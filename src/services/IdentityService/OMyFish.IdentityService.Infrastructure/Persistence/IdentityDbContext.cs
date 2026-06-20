using Microsoft.EntityFrameworkCore;
using OMyFish.IdentityService.Domain.Entities;

namespace OMyFish.IdentityService.Infrastructure.Persistence;

public class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

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
            u.Property(x => x.Role).HasColumnName("role").HasMaxLength(50).HasDefaultValue("user");
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
    }
}
