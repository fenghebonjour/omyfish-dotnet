using OMyFish.ShopService.Application.Dtos;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Commands;

public sealed record UploadProductImageCommand(
    Guid ProductId, Stream Data, long Size, string? ContentType, string? OriginalFileName) : ICommand<ProductDto>;

internal sealed class UploadProductImageCommandHandler(
    IProductRepository productRepo, IImageStorageService storage, IUnitOfWork unitOfWork)
    : ICommandHandler<UploadProductImageCommand, ProductDto>
{
    public async Task<ProductDto> Handle(UploadProductImageCommand command, CancellationToken ct)
    {
        var product = await productRepo.FindByIdAsync(command.ProductId, ct)
                      ?? throw new ProductNotFoundException(command.ProductId);

        var key = await storage.UploadAsync(command.Data, command.Size, command.ContentType, command.OriginalFileName, ct);
        product.SetImage($"/api/v1/shop/products/{command.ProductId}/image", key);

        await unitOfWork.SaveChangesAsync(ct);
        return product.ToDto();
    }
}
