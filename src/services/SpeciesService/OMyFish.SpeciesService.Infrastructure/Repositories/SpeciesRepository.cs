using Microsoft.EntityFrameworkCore;
using OMyFish.SpeciesService.Application.Interfaces;
using OMyFish.SpeciesService.Domain.Entities;
using OMyFish.SpeciesService.Infrastructure.Persistence;

namespace OMyFish.SpeciesService.Infrastructure.Repositories;

public class SpeciesRepository : ISpeciesRepository
{
    private readonly SpeciesDbContext _db;

    public SpeciesRepository(SpeciesDbContext db) => _db = db;

    public Task<Species?> FindByScientificNameAsync(string scientificName, CancellationToken ct = default)
        => _db.Species.FirstOrDefaultAsync(
            s => s.ScientificName.ToLower() == scientificName.ToLower(), ct);

    public async Task<IReadOnlyList<Species>> GetAllAsync(CancellationToken ct = default)
        => await _db.Species.AsNoTracking().ToListAsync(ct);

    public async Task AddAsync(Species species, CancellationToken ct = default)
    {
        _db.Species.Add(species);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddIfNotExistsAsync(Species species, CancellationToken ct = default)
    {
        var exists = await _db.Species.AnyAsync(
            s => s.ScientificName == species.ScientificName, ct);
        if (exists) return;
        _db.Species.Add(species);
        await _db.SaveChangesAsync(ct);
    }
}
