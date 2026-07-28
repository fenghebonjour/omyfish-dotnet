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

        var key = $"identify/{Guid.NewGuid()}/{fileName}";
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
}
