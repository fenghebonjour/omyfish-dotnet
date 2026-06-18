using OMyFish.SpeciesService.Domain.ValueObjects;

namespace OMyFish.SpeciesService.Domain.Entities;

public sealed class Prediction
{
    private Prediction() { }

    public Guid Id { get; private set; }
    public Species Species { get; private set; } = default!;
    public string ImageStorageKey { get; private set; } = default!;
    public ConfidenceScore Confidence { get; private set; } = default!;
    public int Rank { get; private set; }
    public DateTime PredictedAt { get; private set; }

    internal static Prediction Create(Species species, string imageStorageKey, ConfidenceScore confidence, int rank)
    {
        return new Prediction
        {
            Id = Guid.NewGuid(),
            Species = species,
            ImageStorageKey = imageStorageKey,
            Confidence = confidence,
            Rank = rank,
            PredictedAt = DateTime.UtcNow
        };
    }
}
