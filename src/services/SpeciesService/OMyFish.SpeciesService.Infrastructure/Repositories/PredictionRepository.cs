using OMyFish.SpeciesService.Application.Interfaces;
using OMyFish.SpeciesService.Domain.Entities;
using OMyFish.SpeciesService.Infrastructure.Persistence;

namespace OMyFish.SpeciesService.Infrastructure.Repositories;

public class PredictionRepository : IPredictionRepository
{
    private readonly SpeciesDbContext _db;

    public PredictionRepository(SpeciesDbContext db) => _db = db;

    // Stages the insert only — the caller commits via IUnitOfWork so this can share a
    // transaction with an outbox message write (BACKLOG.md item F §2.3).
    public Task AddPredictionAsync(Prediction prediction, CancellationToken ct = default)
    {
        _db.Predictions.Add(prediction);
        return Task.CompletedTask;
    }
}
