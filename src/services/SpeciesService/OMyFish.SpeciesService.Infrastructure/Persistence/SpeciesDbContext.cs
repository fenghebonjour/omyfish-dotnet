using Microsoft.EntityFrameworkCore;
using OMyFish.SpeciesService.Domain.Entities;
using OMyFish.SpeciesService.Domain.ValueObjects;

namespace OMyFish.SpeciesService.Infrastructure.Persistence;

public class SpeciesDbContext : DbContext
{
    public SpeciesDbContext(DbContextOptions<SpeciesDbContext> options) : base(options) { }

    public DbSet<Species> Species => Set<Species>();
    public DbSet<Prediction> Predictions => Set<Prediction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Species>(s =>
        {
            s.ToTable("species");
            s.HasKey(x => x.Id);
            s.Property(x => x.Id).HasColumnName("id");
            s.Property(x => x.ScientificName).HasColumnName("scientific_name").HasMaxLength(255).IsRequired();
            s.HasIndex(x => x.ScientificName).IsUnique();
            s.Property(x => x.CommonName).HasColumnName("common_name").HasMaxLength(255).IsRequired();
            s.Property(x => x.Family).HasColumnName("family").HasMaxLength(255);
            s.Property(x => x.ConservationStatus).HasColumnName("conservation_status").HasMaxLength(50);
            s.Property(x => x.Habitat).HasColumnName("habitat");
            s.Property(x => x.GeographicRange).HasColumnName("geographic_range");
            s.Property(x => x.Description).HasColumnName("description");
            s.Property(x => x.IsNorthAmericanFreshwater).HasColumnName("is_north_american_freshwater");
            s.Property(x => x.ImageUrl).HasColumnName("image_url").HasMaxLength(512);
            s.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            s.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Prediction>(p =>
        {
            p.ToTable("predictions");
            p.HasKey(x => x.Id);
            p.Property(x => x.Id).HasColumnName("id");
            p.Property(x => x.ImageStorageKey).HasColumnName("image_storage_key").HasMaxLength(512).IsRequired();
            p.Property(x => x.Rank).HasColumnName("rank");
            p.Property(x => x.PredictedAt).HasColumnName("predicted_at").HasDefaultValueSql("NOW()");
            p.Property(x => x.Confidence)
                .HasColumnName("confidence")
                .HasConversion(v => v.Value, v => ConfidenceScore.Create(v));
            p.HasOne(x => x.Species)
                .WithMany()
                .HasForeignKey("species_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
