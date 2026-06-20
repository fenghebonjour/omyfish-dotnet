using OMyFish.SpeciesService.Domain.Entities;

namespace OMyFish.SpeciesService.Application.Interfaces;

public interface ISpeciesRepository
{
    Task<Species?> FindByScientificNameAsync(string scientificName, CancellationToken ct = default);
    Task<IReadOnlyList<Species>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Species species, CancellationToken ct = default);
    Task AddIfNotExistsAsync(Species species, CancellationToken ct = default);
}
