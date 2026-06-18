namespace OMyFish.SpeciesService.Domain.ValueObjects;

public sealed record ConfidenceScore
{
    private const double UncertainThreshold = 0.30;
    private const double HighConfidenceThreshold = 0.85;

    public double Value { get; }

    private ConfidenceScore(double value) => Value = value;

    public static ConfidenceScore Create(double value)
    {
        if (value < 0.0 || value > 1.0)
            throw new ArgumentOutOfRangeException(nameof(value),
                $"Confidence score must be between 0.0 and 1.0, was: {value}");
        return new ConfidenceScore(value);
    }

    public bool IsUncertain => Value < UncertainThreshold;
    public bool IsHighConfidence => Value >= HighConfidenceThreshold;

    public string AsPercent() => $"{Value * 100:F1}%";

    public override string ToString() => AsPercent();
}
