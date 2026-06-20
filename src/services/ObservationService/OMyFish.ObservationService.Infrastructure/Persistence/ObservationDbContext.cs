using Microsoft.EntityFrameworkCore;
using OMyFish.ObservationService.Domain.Entities;

namespace OMyFish.ObservationService.Infrastructure.Persistence;

public class ObservationDbContext : DbContext
{
    public ObservationDbContext(DbContextOptions<ObservationDbContext> options) : base(options) { }

    public DbSet<Observation> Observations => Set<Observation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Observation>(o =>
        {
            o.ToTable("observations");
            o.HasKey(x => x.Id);
            o.Property(x => x.Id).HasColumnName("id");
            o.Property(x => x.UserId).HasColumnName("user_id");
            o.Property(x => x.SpeciesName).HasColumnName("species_name").HasMaxLength(255).IsRequired();
            o.Property(x => x.ScientificName).HasColumnName("scientific_name").HasMaxLength(255);
            o.Property(x => x.TopConfidence).HasColumnName("top_confidence");
            o.Property(x => x.ImageStorageKey).HasColumnName("image_storage_key").HasMaxLength(512).IsRequired();
            o.Property(x => x.Latitude).HasColumnName("latitude");
            o.Property(x => x.Longitude).HasColumnName("longitude");
            o.Ignore(x => x.Location);
            o.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
            o.Property(x => x.ObservedAt).HasColumnName("observed_at");
            o.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            o.Ignore(x => x.DomainEvents);
            o.Ignore(x => x.ExifMetadata);
        });
    }
}
