using OMyFish.SpeciesService.Domain.Entities;

namespace OMyFish.SpeciesService.Application.Interfaces;

// Split out of ISpeciesRepository (BACKLOG.md item E): Predictions stay on Postgres, committed
// in the same transaction as the outbox event publish via IUnitOfWork (§2.3), while the species
// catalog itself moved to MongoDB — a store that can't share that transaction.
public interface IPredictionRepository
{
    Task AddPredictionAsync(Prediction prediction, CancellationToken ct = default);
}
