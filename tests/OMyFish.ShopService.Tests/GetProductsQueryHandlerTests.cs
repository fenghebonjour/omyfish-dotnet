using NSubstitute;
using OMyFish.ShopService.Application.Dtos;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Application.Queries;
using OMyFish.ShopService.Application.Queries.Handlers;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.Exceptions;
using Xunit;

namespace OMyFish.ShopService.Tests;

public class GetProductsQueryHandlerTests
{
    private readonly IProductCatalogReader _catalog = Substitute.For<IProductCatalogReader>();
    private readonly IProductCatalogCache _cache = Substitute.For<IProductCatalogCache>();

    [Fact]
    public async Task GetById_CacheHit_SkipsTheDatabase()
    {
        var dto = new ProductDto(Guid.NewGuid(), "Lure", null, 999, null, 5);
        _cache.GetProductAsync(dto.Id, Arg.Any<CancellationToken>()).Returns(dto);

        var result = await new GetProductByIdQueryHandler(_catalog, _cache)
            .Handle(new GetProductByIdQuery(dto.Id), CancellationToken.None);

        Assert.Equal(dto, result);
        await _catalog.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
    }

    [Fact]
    public async Task GetById_Miss_LoadsAndPopulatesTheCache()
    {
        var product = Product.Create("Lure", null, 999, null, 5, true);
        _catalog.FindByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        await new GetProductByIdQueryHandler(_catalog, _cache)
            .Handle(new GetProductByIdQuery(product.Id), CancellationToken.None);

        await _cache.Received(1).PutProductAsync(
            Arg.Is<ProductDto>(d => d.Id == product.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetById_Unknown_MarksMissingSoTheNextLookupSkipsTheDatabase()
    {
        var id = Guid.NewGuid();
        var handler = new GetProductByIdQueryHandler(_catalog, _cache);

        await Assert.ThrowsAsync<ProductNotFoundException>(
            () => handler.Handle(new GetProductByIdQuery(id), CancellationToken.None));

        _cache.Received(1).MarkMissing(id);
    }

    [Fact]
    public async Task List_Miss_LoadsAndPopulatesTheCache()
    {
        _cache.GetListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ProductDto>?)null);
        _catalog.GetAllActiveAsync(Arg.Any<CancellationToken>())
            .Returns([Product.Create("Lure", null, 999, null, 5, true)]);

        var result = await new GetProductsQueryHandler(_catalog, _cache)
            .Handle(new GetProductsQuery(), CancellationToken.None);

        Assert.Single(result);
        await _cache.Received(1).PutListAsync(
            Arg.Is<IReadOnlyList<ProductDto>>(l => l.Count == 1), Arg.Any<CancellationToken>());
    }
}
