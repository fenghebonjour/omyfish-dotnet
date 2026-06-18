using MediatR;

namespace OMyFish.Shared.BuildingBlocks.CQRS;

public interface IQueryHandler<in TQuery, TResult> : IRequestHandler<TQuery, TResult>
    where TQuery : IQuery<TResult> { }
