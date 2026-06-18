namespace OMyFish.ObservationService.Application.Interfaces;

public interface IStorageService
{
    Task<string> UploadAsync(Stream data, string fileName, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string key, CancellationToken ct = default);
    string GetPublicUrl(string key);
}
