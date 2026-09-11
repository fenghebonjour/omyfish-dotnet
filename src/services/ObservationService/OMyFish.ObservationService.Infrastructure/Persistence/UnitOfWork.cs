using OMyFish.ObservationService.Application.Interfaces;

namespace OMyFish.ObservationService.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly ObservationDbContext _db;

    public UnitOfWork(ObservationDbContext db) => _db = db;

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
