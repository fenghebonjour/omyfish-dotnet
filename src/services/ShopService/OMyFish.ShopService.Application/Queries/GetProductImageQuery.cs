using OMyFish.ShopService.Application.Interfaces;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Queries;

public sealed record GetProductImageQuery(Guid ProductId) : IQuery<StoredImage>;
