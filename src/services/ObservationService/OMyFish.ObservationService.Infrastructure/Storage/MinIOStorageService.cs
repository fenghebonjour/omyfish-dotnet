using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;
using OMyFish.ObservationService.Application.Interfaces;

namespace OMyFish.ObservationService.Infrastructure.Storage;

public class MinIOStorageService : IStorageService
{
    private readonly IMinioClient _minio;
    private readonly string _bucket;
    private readonly string _publicEndpoint;

    public MinIOStorageService(IMinioClient minio, IConfiguration config)
    {
        _minio = minio;
        _bucket = config["MinIO__Bucket"] ?? config["MinIO:Bucket"] ?? "omyfish-images";
        _publicEndpoint = config["MinIO__PublicEndpoint"] ?? config["MinIO:PublicEndpoint"] ?? "http://minio:9000";
    }

    public async Task<string> UploadAsync(Stream data, string fileName, string contentType, CancellationToken ct = default)
    {
        await EnsureBucketAsync(ct);

        var key = $"observations/{Guid.NewGuid()}/{fileName}";
        var args = new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(key)
            .WithStreamData(data)
            .WithObjectSize(data.Length)
            .WithContentType(contentType);

        await _minio.PutObjectAsync(args, ct);
        return key;
    }

    public async Task<Stream> DownloadAsync(string key, CancellationToken ct = default)
    {
        var ms = new MemoryStream();
        var args = new GetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(key)
            .WithCallbackStream(stream => stream.CopyTo(ms));

        await _minio.GetObjectAsync(args, ct);
        ms.Position = 0;
        return ms;
    }

    public string GetPublicUrl(string key)
        => $"{_publicEndpoint}/{_bucket}/{key}";

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        var exists = await _minio.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_bucket), ct);
        if (!exists)
            await _minio.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(_bucket), ct);
    }
}
