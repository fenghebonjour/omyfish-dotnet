using Microsoft.EntityFrameworkCore;
using OMyFish.ObservationService.Application.Interfaces;
using OMyFish.ObservationService.Domain.Entities;
using OMyFish.ObservationService.Infrastructure.Persistence;

namespace OMyFish.ObservationService.Infrastructure.Repositories;

public class ObservationRepository : IObservationRepository
{
    private readonly ObservationDbContext _db;

    public ObservationRepository(ObservationDbContext db) => _db = db;

    public Task<Observation?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Observations.FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<Observation>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await _db.Observations.Where(o => o.UserId == userId).AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<Observation>> GetAllAsync(CancellationToken ct = default)
        => await _db.Observations.AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<Observation>> GetWithLocationAsync(CancellationToken ct = default)
        => await _db.Observations
            .AsNoTracking()
            .Where(o => o.Latitude != null && o.Longitude != null)
            .OrderByDescending(o => o.ObservedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Observation observation, CancellationToken ct = default)
    {
        _db.Observations.Add(observation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var obs = await _db.Observations.FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, ct);
        if (obs is null) return false;
        _db.Observations.Remove(obs);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
