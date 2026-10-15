using OMyFish.ShopService.Application.Dtos;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Queries;

public sealed record GetProductsQuery : IQuery<IReadOnlyList<ProductDto>>;

public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto>;
