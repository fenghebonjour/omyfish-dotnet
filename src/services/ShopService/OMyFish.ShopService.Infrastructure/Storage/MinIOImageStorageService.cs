using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;
using OMyFish.ShopService.Application.Interfaces;

namespace OMyFish.ShopService.Infrastructure.Storage;

public class MinIOImageStorageService : IImageStorageService
{
    private const string DefaultExtension = ".jpg";

    private readonly IMinioClient _minio;
    private readonly string _bucket;
    private volatile bool _bucketReady;

    public MinIOImageStorageService(IMinioClient minio, IConfiguration config)
    {
        _minio = minio;
        _bucket = config["MinIO__Bucket"] ?? config["MinIO:Bucket"] ?? "product-images";
    }

    // Deferred to first actual upload rather than startup — MinIO being unreachable at startup
    // must only fail an upload attempt, never crash the whole service.
    private async Task EnsureBucketExistsAsync(CancellationToken ct)
    {
        if (_bucketReady) return;
        var exists = await _minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), ct);
        if (!exists)
            await _minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), ct);
        _bucketReady = true;
    }

    public async Task<string> UploadAsync(
        Stream data, long size, string? contentType, string? originalFileName, CancellationToken ct = default)
    {
        await EnsureBucketExistsAsync(ct);

        var key = $"products/{Guid.NewGuid()}{ExtensionOf(originalFileName)}";
        var args = new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(key)
            .WithStreamData(data)
            .WithObjectSize(size)
            .WithContentType(contentType ?? "image/jpeg");

        await _minio.PutObjectAsync(args, ct);
        return key;
    }

    public async Task<StoredImage> DownloadAsync(string key, CancellationToken ct = default)
    {
        var ms = new MemoryStream();
        var contentType = "application/octet-stream";
        var args = new GetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(key)
            .WithCallbackStream(stream => stream.CopyTo(ms));

        var stat = await _minio.GetObjectAsync(args, ct);
        if (!string.IsNullOrEmpty(stat.ContentType))
            contentType = stat.ContentType;

        ms.Position = 0;
        return new StoredImage(ms, contentType);
    }

    // Only a short alphanumeric extension is kept, so a client-supplied filename like
    // "fish.jpg/../../etc/passwd" can't put separators into the object key.
    private static string ExtensionOf(string? originalFileName)
    {
        if (originalFileName is null) return DefaultExtension;
        var dot = originalFileName.LastIndexOf('.');
        if (dot < 0) return DefaultExtension;
        var ext = originalFileName[(dot + 1)..].ToLowerInvariant();
        return ext.Length is > 0 and <= 8 && ext.All(char.IsLetterOrDigit) ? $".{ext}" : DefaultExtension;
    }
}
