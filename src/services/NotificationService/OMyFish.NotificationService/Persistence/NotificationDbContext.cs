using Microsoft.EntityFrameworkCore;
using OMyFish.NotificationService.Entities;

namespace OMyFish.NotificationService.Persistence;

public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options) : base(options) { }

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(n =>
        {
            n.ToTable("notifications");
            n.HasKey(x => x.Id);
            n.Property(x => x.Id).HasColumnName("id");
            n.Property(x => x.UserId).HasColumnName("user_id");
            n.Property(x => x.Type).HasColumnName("type").HasMaxLength(64).IsRequired();
            n.Property(x => x.Title).HasColumnName("title").HasMaxLength(255).IsRequired();
            n.Property(x => x.Body).HasColumnName("body");
            n.Property(x => x.IsRead).HasColumnName("is_read");
            n.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        });
    }
}
