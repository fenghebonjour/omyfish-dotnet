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
            u.Property(x => x.Email).HasMaxLength(255).IsRequired();
            u.HasIndex(x => x.Email).IsUnique();
            u.Property(x => x.HashedPassword).HasMaxLength(255).IsRequired();
            u.Property(x => x.DisplayName).HasMaxLength(100);
            u.Property(x => x.Role).HasMaxLength(50).HasDefaultValue("user");
            u.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            u.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
        });

        modelBuilder.Entity<ApiKey>(k =>
        {
            k.ToTable("api_keys");
            k.HasKey(x => x.Id);
            k.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();
            k.HasIndex(x => x.KeyHash).IsUnique();
            k.Property(x => x.Name).HasMaxLength(100);
            k.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
