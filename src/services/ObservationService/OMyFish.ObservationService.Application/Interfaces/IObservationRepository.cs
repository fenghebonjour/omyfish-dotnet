using OMyFish.ObservationService.Application.Queries;
using OMyFish.ObservationService.Domain.Entities;

namespace OMyFish.ObservationService.Application.Interfaces;

public interface IObservationRepository
{
    Task<Observation?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Observation>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Observation>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Observation>> GetWithLocationAsync(CancellationToken ct = default);
    Task<IReadOnlyList<NearbyObservationDto>> GetNearbyAsync(double latitude, double longitude, double radiusKm, CancellationToken ct = default);
    Task AddAsync(Observation observation, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default);
}
