using OMyFish.ShopService.Application.Dtos;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Queries.Handlers;

internal sealed class GetProductsQueryHandler(IProductCatalogReader catalog, IProductCatalogCache cache)
    : IQueryHandler<GetProductsQuery, IReadOnlyList<ProductDto>>
{
    public async Task<IReadOnlyList<ProductDto>> Handle(GetProductsQuery query, CancellationToken ct)
    {
        if (await cache.GetListAsync(ct) is { } cached) return cached;

        var products = (await catalog.GetAllActiveAsync(ct)).Select(p => p.ToDto()).ToList();
        await cache.PutListAsync(products, ct);
        return products;
    }
}

internal sealed class GetProductByIdQueryHandler(IProductCatalogReader catalog, IProductCatalogCache cache)
    : IQueryHandler<GetProductByIdQuery, ProductDto>
{
    public async Task<ProductDto> Handle(GetProductByIdQuery query, CancellationToken ct)
    {
        if (cache.IsKnownMissing(query.Id)) throw new ProductNotFoundException(query.Id);
        if (await cache.GetProductAsync(query.Id, ct) is { } cached) return cached;

        var product = await catalog.FindByIdAsync(query.Id, ct);
        if (product is null)
        {
            cache.MarkMissing(query.Id);
            throw new ProductNotFoundException(query.Id);
        }

        var dto = product.ToDto();
        await cache.PutProductAsync(dto, ct);
        return dto;
    }
}
