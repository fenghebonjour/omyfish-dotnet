using OMyFish.SpeciesService.Domain.Entities;

namespace OMyFish.SpeciesService.Application.Interfaces;

public interface ISpeciesRepository
{
    Task<Species?> FindByScientificNameAsync(string scientificName, CancellationToken ct = default);

    // Batched lookup for identify's per-prediction loop (BACKLOG.md item F, WEAKNESS_AUDIT.md §3.4)
    // — avoids one DB round-trip per AI prediction.
    Task<IReadOnlyList<Species>> FindByScientificNamesAsync(IEnumerable<string> scientificNames, CancellationToken ct = default);
    Task<IReadOnlyList<Species>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Species species, CancellationToken ct = default);
    Task AddIfNotExistsAsync(Species species, CancellationToken ct = default);
    Task AddPredictionAsync(Prediction prediction, CancellationToken ct = default);
}
