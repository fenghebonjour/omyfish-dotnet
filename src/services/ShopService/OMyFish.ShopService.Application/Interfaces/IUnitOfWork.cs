namespace OMyFish.ShopService.Application.Interfaces;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);

    /// Runs work inside one DB transaction, so immediately-executed statements (the atomic stock
    /// updates on IProductRepository) commit or roll back together with what SaveChangesAsync
    /// writes inside it. On failure, the transaction rolls back and pending tracked changes are
    /// discarded.
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default);
}
