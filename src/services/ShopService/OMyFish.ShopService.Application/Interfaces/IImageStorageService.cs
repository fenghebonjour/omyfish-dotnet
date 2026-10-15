namespace OMyFish.ShopService.Application.Interfaces;

public interface IImageStorageService
{
    // Returns an opaque storage key — never a browsable URL. Product.ImageUrl points at this
    // service's own serve-image endpoint instead, so MinIO itself never needs to be exposed.
    Task<string> UploadAsync(
        Stream data, long size, string? contentType, string? originalFileName, CancellationToken ct = default);

    Task<StoredImage> DownloadAsync(string key, CancellationToken ct = default);
}

public sealed record StoredImage(Stream Data, string ContentType);
