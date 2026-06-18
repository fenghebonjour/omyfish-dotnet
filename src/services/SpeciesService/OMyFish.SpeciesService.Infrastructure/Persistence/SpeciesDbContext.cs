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
            s.Property(x => x.ScientificName).HasMaxLength(255).IsRequired();
            s.HasIndex(x => x.ScientificName).IsUnique();
            s.Property(x => x.CommonName).HasMaxLength(255).IsRequired();
            s.Property(x => x.Family).HasMaxLength(255);
            s.Property(x => x.ConservationStatus).HasMaxLength(50);
            s.Property(x => x.ImageUrl).HasMaxLength(512);
            s.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            s.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Prediction>(p =>
        {
            p.ToTable("predictions");
            p.HasKey(x => x.Id);
            p.Property(x => x.ImageStorageKey).HasMaxLength(512).IsRequired();
            p.Property(x => x.Rank);
            p.Property(x => x.PredictedAt).HasDefaultValueSql("NOW()");
            p.Property(x => x.Confidence)
                .HasConversion(v => v.Value, v => ConfidenceScore.Create(v));
            p.HasOne(x => x.Species)
                .WithMany()
                .HasForeignKey("species_id")
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
