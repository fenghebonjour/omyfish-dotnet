using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Domain.Interfaces;
using OMyFish.IdentityService.Infrastructure.Persistence;

namespace OMyFish.IdentityService.Infrastructure.Repositories;

public class ApiKeyRepository : IApiKeyRepository
{
    private readonly IdentityDbContext _db;

    public ApiKeyRepository(IdentityDbContext db) => _db = db;

    public async Task<ApiKey> CreateAsync(ApiKey apiKey, CancellationToken ct = default)
    {
        _db.ApiKeys.Add(apiKey);
        await _db.SaveChangesAsync(ct);
        return apiKey;
    }
}
