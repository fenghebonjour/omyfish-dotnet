using Microsoft.EntityFrameworkCore;
using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Domain.Interfaces;
using OMyFish.IdentityService.Infrastructure.Persistence;

namespace OMyFish.IdentityService.Infrastructure.Repositories;

public class ProcessedWebhookEventRepository : IProcessedWebhookEventRepository
{
    private readonly IdentityDbContext _db;

    public ProcessedWebhookEventRepository(IdentityDbContext db) => _db = db;

    public Task<bool> ExistsAsync(string eventId, CancellationToken ct = default)
        => _db.ProcessedWebhookEvents.AnyAsync(e => e.EventId == eventId, ct);

    public async Task SaveAsync(ProcessedWebhookEvent evt, CancellationToken ct = default)
    {
        _db.ProcessedWebhookEvents.Add(evt);
        await _db.SaveChangesAsync(ct);
    }
}
