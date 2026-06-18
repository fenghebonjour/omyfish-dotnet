using MediatR;

namespace OMyFish.Shared.BuildingBlocks.CQRS;

public interface ICommand : IRequest { }

public interface ICommand<out TResult> : IRequest<TResult> { }
