using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OMyFish.ShopService.Application.Dtos;
using OMyFish.ShopService.Infrastructure.Caching;
using StackExchange.Redis;
using Xunit;

namespace OMyFish.ShopService.Tests;

public class ProductCatalogCacheTests
{
    private static readonly ProductDto Lure = new(Guid.NewGuid(), "Lure", null, 999, null, 5);

    private static ProductCatalogCache Cache(IConnectionMultiplexer? redis) =>
        new(new MemoryCache(new MemoryCacheOptions()), redis, NullLogger<ProductCatalogCache>.Instance);

    private static (IConnectionMultiplexer, IDatabase) Redis()
    {
        var mux = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        mux.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        return (mux, db);
    }

    [Fact]
    public async Task Put_ThenGet_ServesFromL1()
    {
        var cache = Cache(redis: null);

        await cache.PutProductAsync(Lure);

        Assert.Equal(Lure, await cache.GetProductAsync(Lure.Id));
    }

    [Fact]
    public async Task L1Miss_L2Hit_ReturnsTheRedisValue()
    {
        var (mux, db) = Redis();
        db.StringGetAsync($"shop:product:{Lure.Id}", Arg.Any<CommandFlags>())
            .Returns(new RedisValue(JsonSerializer.Serialize(Lure)));

        Assert.Equal(Lure, await Cache(mux).GetProductAsync(Lure.Id));
    }

    [Fact]
    public async Task RedisDown_FallsThroughAsAMissInsteadOfThrowing()
    {
        var (mux, db) = Redis();
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>(),
                Arg.Any<When>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var cache = Cache(mux);

        Assert.Null(await cache.GetListAsync());
        await cache.PutListAsync([Lure]); // must not throw either
        Assert.Single((await cache.GetListAsync())!); // still served from L1
    }

    [Fact]
    public async Task EvictProduct_AlsoEvictsTheList()
    {
        var cache = Cache(redis: null);
        await cache.PutProductAsync(Lure);
        await cache.PutListAsync([Lure]);

        await cache.EvictProductAsync(Lure.Id);

        Assert.Null(await cache.GetProductAsync(Lure.Id));
        Assert.Null(await cache.GetListAsync());
    }

    [Fact]
    public void MarkMissing_IsRemembered()
    {
        var cache = Cache(redis: null);
        var id = Guid.NewGuid();

        cache.MarkMissing(id);

        Assert.True(cache.IsKnownMissing(id));
        Assert.False(cache.IsKnownMissing(Guid.NewGuid()));
    }
}
