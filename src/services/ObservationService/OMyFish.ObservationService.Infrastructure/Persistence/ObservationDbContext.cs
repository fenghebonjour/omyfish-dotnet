using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using OMyFish.ObservationService.Domain.Entities;
using OMyFish.ObservationService.Domain.ValueObjects;

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
            o.Property(x => x.UserId);
            o.Property(x => x.SpeciesName).HasMaxLength(255).IsRequired();
            o.Property(x => x.ScientificName).HasMaxLength(255);
            o.Property(x => x.TopConfidence);
            o.Property(x => x.ImageStorageKey).HasMaxLength(512).IsRequired();
            o.Property(x => x.Notes).HasMaxLength(2000);
            o.Property(x => x.ObservedAt);
            o.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            o.Ignore(x => x.DomainEvents);

            // Store GPS as PostGIS Point
            o.Property<Point?>("location_geom")
                .HasColumnName("location_geom")
                .HasColumnType("geometry(Point,4326)");

            // Map GpsCoordinates value object via shadow property
            o.Ignore(x => x.Location);

            // Store ExifMetadata as JSON columns
            o.Property<DateTime?>("exif_captured_at").HasColumnName("exif_captured_at");
            o.Property<string?>("exif_camera_model").HasMaxLength(255).HasColumnName("exif_camera_model");
            o.Ignore(x => x.ExifMetadata);
        });
    }
}
