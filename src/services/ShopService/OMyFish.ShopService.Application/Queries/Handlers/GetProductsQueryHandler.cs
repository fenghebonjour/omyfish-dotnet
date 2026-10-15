using OMyFish.ShopService.Application.Dtos;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Queries.Handlers;

internal sealed class GetProductsQueryHandler(IProductRepository repo)
    : IQueryHandler<GetProductsQuery, IReadOnlyList<ProductDto>>
{
    public async Task<IReadOnlyList<ProductDto>> Handle(GetProductsQuery query, CancellationToken ct) =>
        (await repo.GetAllActiveAsync(ct)).Select(p => p.ToDto()).ToList();
}

internal sealed class GetProductByIdQueryHandler(IProductRepository repo)
    : IQueryHandler<GetProductByIdQuery, ProductDto>
{
    public async Task<ProductDto> Handle(GetProductByIdQuery query, CancellationToken ct)
    {
        var product = await repo.FindByIdAsync(query.Id, ct) ?? throw new ProductNotFoundException(query.Id);
        return product.ToDto();
    }
}
