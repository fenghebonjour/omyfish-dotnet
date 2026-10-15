using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Queries.Handlers;

internal sealed class GetProductImageQueryHandler(IProductRepository repo, IImageStorageService storage)
    : IQueryHandler<GetProductImageQuery, StoredImage>
{
    public async Task<StoredImage> Handle(GetProductImageQuery query, CancellationToken ct)
    {
        var product = await repo.FindByIdAsync(query.ProductId, ct) ?? throw new ProductNotFoundException(query.ProductId);
        if (product.ImageStorageKey is null)
            throw new ProductNotFoundException(query.ProductId);
        return await storage.DownloadAsync(product.ImageStorageKey, ct);
    }
}
