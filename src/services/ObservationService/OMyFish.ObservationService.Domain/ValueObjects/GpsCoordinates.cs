namespace OMyFish.ObservationService.Domain.ValueObjects;

public sealed record GpsCoordinates
{
    public double Latitude { get; }
    public double Longitude { get; }

    private GpsCoordinates(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public static GpsCoordinates Create(double latitude, double longitude)
    {
        if (latitude < -90 || latitude > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), $"Latitude must be between -90 and 90, was: {latitude}");
        if (longitude < -180 || longitude > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), $"Longitude must be between -180 and 180, was: {longitude}");

        return new GpsCoordinates(latitude, longitude);
    }

    public string ToWkt() => $"POINT({Longitude} {Latitude})";
}
