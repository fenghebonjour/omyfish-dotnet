namespace OMyFish.ObservationService.Domain.ValueObjects;

public sealed record ExifMetadata(
    DateTime? CapturedAt,
    string? CameraModel,
    int? ImageWidth,
    int? ImageHeight,
    double? FocalLength,
    double? Aperture,
    int? Iso)
{
    public static ExifMetadata Empty() =>
        new(null, null, null, null, null, null, null);
}
