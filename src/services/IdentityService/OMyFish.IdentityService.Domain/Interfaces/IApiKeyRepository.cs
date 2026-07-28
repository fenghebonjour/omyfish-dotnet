using OMyFish.IdentityService.Domain.Entities;

namespace OMyFish.IdentityService.Domain.Interfaces;

public interface IApiKeyRepository
{
    Task<ApiKey> CreateAsync(ApiKey apiKey, CancellationToken ct = default);
}
