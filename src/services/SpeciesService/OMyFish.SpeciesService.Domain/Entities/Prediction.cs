using OMyFish.SpeciesService.Domain.ValueObjects;

namespace OMyFish.SpeciesService.Domain.Entities;

public sealed class Prediction
{
    private Prediction() { }

    public Guid Id { get; private set; }
    public string ScientificName { get; private set; } = default!;
    public string ImageStorageKey { get; private set; } = default!;
    public ConfidenceScore Confidence { get; private set; } = default!;
    public int Rank { get; private set; }
    public DateTime PredictedAt { get; private set; }

    // Takes the Species object (rather than just its scientific name) for convenience at the
    // call site, but only the name is stored — Species now lives in MongoDB (BACKLOG.md item
    // E) while Prediction stays in Postgres alongside the outbox (§2.3), so there's no FK/
    // navigation between them any more.
    internal static Prediction Create(Species species, string imageStorageKey, ConfidenceScore confidence, int rank)
    {
        return new Prediction
        {
            Id = Guid.NewGuid(),
            ScientificName = species.ScientificName,
            ImageStorageKey = imageStorageKey,
            Confidence = confidence,
            Rank = rank,
            PredictedAt = DateTime.UtcNow
        };
    }
}
