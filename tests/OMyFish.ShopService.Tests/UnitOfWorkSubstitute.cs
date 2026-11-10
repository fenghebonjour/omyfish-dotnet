using NSubstitute;
using OMyFish.ShopService.Application.Interfaces;

namespace OMyFish.ShopService.Tests;

internal static class UnitOfWorkSubstitute
{
    /// An IUnitOfWork mock whose ExecuteInTransactionAsync<T> just runs the work it's given, for
    /// each result type a handler under test uses.
    public static IUnitOfWork Create()
    {
        var uow = Substitute.For<IUnitOfWork>();
        RunsWork<bool>(uow);
        RunsWork<OMyFish.ShopService.Domain.Entities.Order>(uow);
        RunsWork<OMyFish.ShopService.Application.Dtos.OrderDto>(uow);
        return uow;
    }

    private static void RunsWork<T>(IUnitOfWork uow) =>
        uow.ExecuteInTransactionAsync(Arg.Any<Func<Task<T>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<T>>>()());
}
