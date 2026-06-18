using MediatR;

namespace OMyFish.Shared.BuildingBlocks.CQRS;

public interface IQuery<out TResult> : IRequest<TResult> { }
