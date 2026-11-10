using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;
using OMyFish.SpeciesService.Application.Interfaces;

namespace OMyFish.SpeciesService.Infrastructure.Storage;

public class MinIOStorageService : IStorageService
{
    private readonly IMinioClient _minio;
    private readonly string _bucket;

    public MinIOStorageService(IMinioClient minio, IConfiguration config)
    {
        _minio = minio;
        _bucket = config["MinIO__Bucket"] ?? config["MinIO:Bucket"] ?? "omyfish-images";
    }

    public async Task<string> UploadAsync(Stream data, string fileName, string contentType, CancellationToken ct = default)
    {
        await EnsureBucketAsync(ct);

        var key = $"identify/{Guid.NewGuid()}{ExtensionOf(fileName)}";
        var args = new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(key)
            .WithStreamData(data)
            .WithObjectSize(data.Length)
            .WithContentType(contentType);

        await _minio.PutObjectAsync(args, ct);
        return key;
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        var exists = await _minio.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_bucket), ct);
        if (!exists)
            await _minio.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(_bucket), ct);
    }

    private const string DefaultExtension = ".jpg";

    // Only a short alphanumeric extension of the client filename is kept, so a name like
    // "fish.jpg/../../evil" can't put path separators into the object key.
    private static string ExtensionOf(string? fileName)
    {
        if (fileName is null) return DefaultExtension;
        var dot = fileName.LastIndexOf('.');
        if (dot < 0) return DefaultExtension;
        var e = fileName[(dot + 1)..].ToLowerInvariant();
        return e.Length is > 0 and <= 8 && e.All(char.IsAsciiLetterOrDigit) ? $".{e}" : DefaultExtension;
    }
}
