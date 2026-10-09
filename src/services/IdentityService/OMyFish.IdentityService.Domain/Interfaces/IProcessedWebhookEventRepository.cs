using OMyFish.IdentityService.Domain.Entities;

namespace OMyFish.IdentityService.Domain.Interfaces;

public interface IProcessedWebhookEventRepository
{
    Task<bool> ExistsAsync(string eventId, CancellationToken ct = default);
    Task SaveAsync(ProcessedWebhookEvent evt, CancellationToken ct = default);
}
