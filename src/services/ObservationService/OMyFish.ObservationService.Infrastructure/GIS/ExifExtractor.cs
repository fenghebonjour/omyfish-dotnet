using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using OMyFish.ObservationService.Domain.ValueObjects;

namespace OMyFish.ObservationService.Infrastructure.GIS;

public static class ExifExtractor
{
    public static (ExifMetadata? Metadata, GpsCoordinates? Gps) Extract(Stream imageStream)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(imageStream);

            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var gpsDir = directories.OfType<GpsDirectory>().FirstOrDefault();

            DateTime? capturedAt = null;
            if (subIfd?.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt) == true)
                capturedAt = dt;

            string? cameraModel = ifd0?.GetString(ExifDirectoryBase.TagModel);

            int? width = null, height = null;
            if (subIfd?.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var w) == true) width = w;
            if (subIfd?.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var h) == true) height = h;

            double? focalLength = null, aperture = null;
            if (subIfd?.TryGetDouble(ExifDirectoryBase.TagFocalLength, out var fl) == true) focalLength = fl;
            if (subIfd?.TryGetDouble(ExifDirectoryBase.TagFNumber, out var fn) == true) aperture = fn;

            int? iso = null;
            if (subIfd?.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var isoVal) == true) iso = isoVal;

            var metadata = new ExifMetadata(capturedAt, cameraModel, width, height, focalLength, aperture, iso);

            GpsCoordinates? gps = null;
            var geoLocation = gpsDir?.GetGeoLocation();
            if (geoLocation is not null)
            {
                try { gps = GpsCoordinates.Create(geoLocation.Latitude, geoLocation.Longitude); }
                catch { /* Invalid coords — skip */ }
            }

            return (metadata, gps);
        }
        catch
        {
            return (null, null);
        }
    }
}
