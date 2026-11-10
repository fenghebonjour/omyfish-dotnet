using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OMyFish.ShopService.Application.Dtos;
using OMyFish.ShopService.Application.Interfaces;
using Prometheus;
using StackExchange.Redis;

namespace OMyFish.ShopService.Infrastructure.Caching;

/// Two-level read cache for the product catalog: in-process memory L1 (no network hop) backed
/// by a shared Redis L2 (so N replicas don't all hit Postgres whenever their own L1 is cold).
/// Port of omyfish-java's ProductCache, same TTLs:
///  - Jittered TTLs on both levels, so entries written together (e.g. every L1 cold right
///    after a deploy) don't all expire in the same instant and stampede Redis/Postgres
///    (cache avalanche).
///  - A short L1-only negative cache for unknown ids (cache penetration).
///  - Every Redis call is wrapped: a Redis outage falls through to the database instead of
///    throwing. With no Redis configured at all (redis is null) it runs L1-only.
public sealed class ProductCatalogCache : IProductCatalogCache
{
    private static readonly TimeSpan L1Ttl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan L1Jitter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan L2Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan L2Jitter = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromSeconds(10);

    private const string ListKey = "shop:product-list";
    private const string ProductPrefix = "shop:product:";
    private const string MissingPrefix = "shop:missing:";

    private static readonly Counter Requests = Metrics.CreateCounter(
        "shop_cache_requests_total", "Product catalog cache lookups by cache and result.",
        new CounterConfiguration { LabelNames = ["cache", "result"] });

    private readonly IMemoryCache _l1;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<ProductCatalogCache> _logger;

    public ProductCatalogCache(IMemoryCache l1, IConnectionMultiplexer? redis, ILogger<ProductCatalogCache> logger)
    {
        _l1 = l1;
        _redis = redis;
        _logger = logger;
    }

    public bool IsKnownMissing(Guid id) => _l1.TryGetValue(MissingPrefix + id, out _);

    public void MarkMissing(Guid id) => _l1.Set(MissingPrefix + id, true, NegativeTtl);

    public Task<ProductDto?> GetProductAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<ProductDto>(ProductPrefix + id, "product");

    public Task PutProductAsync(ProductDto product, CancellationToken ct = default) =>
        PutAsync(ProductPrefix + product.Id, product);

    public async Task EvictProductAsync(Guid id, CancellationToken ct = default)
    {
        _l1.Remove(ProductPrefix + id);
        _l1.Remove(ListKey);
        await RedisAsync(db => db.KeyDeleteAsync([ProductPrefix + id, ListKey]), "delete", ProductPrefix + id);
    }

    public async Task<IReadOnlyList<ProductDto>?> GetListAsync(CancellationToken ct = default) =>
        await GetAsync<List<ProductDto>>(ListKey, "list");

    public Task PutListAsync(IReadOnlyList<ProductDto> products, CancellationToken ct = default) =>
        PutAsync(ListKey, products.ToList());

    private async Task<T?> GetAsync<T>(string key, string cacheName) where T : class
    {
        if (_l1.TryGetValue(key, out T? hit))
        {
            Requests.WithLabels(cacheName, "l1_hit").Inc();
            return hit;
        }

        var json = await RedisAsync(db => db.StringGetAsync(key), "read", key);
        T? fromRedis = null;
        if (json is { HasValue: true })
        {
            try
            {
                fromRedis = JsonSerializer.Deserialize<T>(json.ToString());
            }
            catch (JsonException e)
            {
                _logger.LogWarning(e, "Redis value for {Key} could not be parsed — falling through to the database", key);
            }
        }

        if (fromRedis is not null)
        {
            _l1.Set(key, fromRedis, Jittered(L1Ttl, L1Jitter));
            Requests.WithLabels(cacheName, "l2_hit").Inc();
            return fromRedis;
        }

        Requests.WithLabels(cacheName, "miss").Inc();
        return null;
    }

    private async Task PutAsync<T>(string key, T value) where T : class
    {
        _l1.Set(key, value, Jittered(L1Ttl, L1Jitter));
        var json = JsonSerializer.Serialize(value);
        await RedisAsync(db => db.StringSetAsync(key, json, Jittered(L2Ttl, L2Jitter)), "write", key);
    }

    private async Task<TResult?> RedisAsync<TResult>(Func<IDatabase, Task<TResult>> op, string action, string key)
    {
        if (_redis is null) return default;
        try
        {
            return await op(_redis.GetDatabase());
        }
        catch (Exception e) when (e is RedisException or TimeoutException or ObjectDisposedException)
        {
            _logger.LogWarning(e, "Redis {Action} failed for {Key} — falling through to L1/the database", action, key);
            return default;
        }
    }

    private static TimeSpan Jittered(TimeSpan baseTtl, TimeSpan jitter)
    {
        var deltaMs = Random.Shared.NextDouble() * 2 * jitter.TotalMilliseconds - jitter.TotalMilliseconds;
        return baseTtl + TimeSpan.FromMilliseconds(deltaMs);
    }
}
