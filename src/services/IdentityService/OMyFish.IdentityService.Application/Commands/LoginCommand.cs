using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.IdentityService.Domain.Interfaces;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Commands;

public sealed record LoginCommand(string Email, string Password) : ICommand<LoginResult?>;

public sealed record LoginResult(
    string AccessToken, string RefreshToken, Guid UserId, string Email, string Role);

internal sealed class LoginCommandHandler : ICommandHandler<LoginCommand, LoginResult?>
{
    private readonly IUserRepository _users;
    private readonly ITokenService _tokens;

    public LoginCommandHandler(IUserRepository users, ITokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    public async Task<LoginResult?> Handle(LoginCommand command, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(command.Email, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(command.Password, user.HashedPassword))
            return null;
        if (!user.IsActive)
            throw new InactiveAccountException("Account is deactivated.");

        return new LoginResult(
            _tokens.CreateAccessToken(user), _tokens.CreateRefreshToken(user),
            user.Id, user.Email, user.Role);
    }
}

/// The credentials are valid but the account is deactivated. ->403 at the endpoint, distinct
/// from invalid credentials (null result, ->401).
public sealed class InactiveAccountException(string message) : Exception(message);
