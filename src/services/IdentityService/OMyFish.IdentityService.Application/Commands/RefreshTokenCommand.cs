using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.IdentityService.Domain.Interfaces;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Commands;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<LoginResult?>;

internal sealed class RefreshTokenCommandHandler : ICommandHandler<RefreshTokenCommand, LoginResult?>
{
    private readonly IUserRepository _users;
    private readonly ITokenService _tokens;

    public RefreshTokenCommandHandler(IUserRepository users, ITokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    public async Task<LoginResult?> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        var userId = _tokens.ValidateRefreshToken(command.RefreshToken);
        if (userId is null) return null;

        var user = await _users.FindByIdAsync(userId.Value, ct);
        if (user is null || !user.IsActive) return null;

        return new LoginResult(
            _tokens.CreateAccessToken(user), _tokens.CreateRefreshToken(user),
            user.Id, user.Email, user.Role);
    }
}
