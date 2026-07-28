namespace OMyFish.SpeciesService.Application.Interfaces;

public interface IStorageService
{
    Task<string> UploadAsync(Stream data, string fileName, string contentType, CancellationToken ct = default);
}
