using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Domain.Interfaces;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Commands;

public sealed record CreateApiKeyCommand(Guid UserId, string Name) : ICommand<CreateApiKeyResult?>;

public sealed record CreateApiKeyResult(Guid KeyId, string PlainKey, string Name);

internal sealed class CreateApiKeyCommandHandler : ICommandHandler<CreateApiKeyCommand, CreateApiKeyResult?>
{
    private readonly IUserRepository _users;
    private readonly IApiKeyRepository _apiKeys;

    public CreateApiKeyCommandHandler(IUserRepository users, IApiKeyRepository apiKeys)
    {
        _users = users;
        _apiKeys = apiKeys;
    }

    public async Task<CreateApiKeyResult?> Handle(CreateApiKeyCommand command, CancellationToken ct)
    {
        if (await _users.FindByIdAsync(command.UserId, ct) is null) return null;

        var raw = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var plainKey = "omf_" + Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var keyHash = BCrypt.Net.BCrypt.HashPassword(plainKey);

        var apiKey = ApiKey.Create(command.UserId, keyHash, command.Name);
        await _apiKeys.CreateAsync(apiKey, ct);

        return new CreateApiKeyResult(apiKey.Id, plainKey, apiKey.Name);
    }
}
