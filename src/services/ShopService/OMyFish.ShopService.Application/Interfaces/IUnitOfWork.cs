namespace OMyFish.ShopService.Application.Interfaces;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}
