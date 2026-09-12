using MassTransit;
using Microsoft.EntityFrameworkCore;
using OMyFish.SpeciesService.Domain.Entities;
using OMyFish.SpeciesService.Domain.ValueObjects;

namespace OMyFish.SpeciesService.Infrastructure.Persistence;

// The species catalog itself lives in MongoDB now (BACKLOG.md item E) — this DbContext only
// holds Predictions, because those still need to commit in the same Postgres transaction as
// the MassTransit outbox message on every /identify call (§2.3), which Mongo can't take part in.
public class SpeciesDbContext : DbContext
{
    public SpeciesDbContext(DbContextOptions<SpeciesDbContext> options) : base(options) { }

    public DbSet<Prediction> Predictions => Set<Prediction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Prediction>(p =>
        {
            p.ToTable("predictions");
            p.HasKey(x => x.Id);
            p.Property(x => x.Id).HasColumnName("id");
            p.Property(x => x.ScientificName).HasColumnName("scientific_name").HasMaxLength(255).IsRequired();
            p.Property(x => x.ImageStorageKey).HasColumnName("image_storage_key").HasMaxLength(512).IsRequired();
            p.Property(x => x.Rank).HasColumnName("rank");
            p.Property(x => x.PredictedAt).HasColumnName("predicted_at").HasDefaultValueSql("NOW()");
            p.Property(x => x.Confidence)
                .HasColumnName("confidence")
                .HasConversion(v => v.Value, v => ConfidenceScore.Create(v));
        });

        // MassTransit's EF Core transactional outbox — publishing an integration event and
        // saving the triggering domain write now commit in the same transaction, so a crash
        // between the two can no longer drop the event (BACKLOG.md item F §2.3). Tables are
        // suffixed per-service since species/observation/notification all share one physical
        // Postgres database (same reason schemaversions_<service> is per-service — see CLAUDE.md).
        modelBuilder.AddInboxStateEntity(e => e.ToTable("inbox_state_species"));
        modelBuilder.AddOutboxMessageEntity(e => e.ToTable("outbox_message_species"));
        modelBuilder.AddOutboxStateEntity(e => e.ToTable("outbox_state_species"));
    }
}
