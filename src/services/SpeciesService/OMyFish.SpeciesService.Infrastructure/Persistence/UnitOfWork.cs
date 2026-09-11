using OMyFish.SpeciesService.Application.Interfaces;

namespace OMyFish.SpeciesService.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly SpeciesDbContext _db;

    public UnitOfWork(SpeciesDbContext db) => _db = db;

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
