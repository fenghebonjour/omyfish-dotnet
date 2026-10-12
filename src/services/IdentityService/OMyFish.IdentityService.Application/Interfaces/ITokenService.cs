using OMyFish.IdentityService.Domain.Entities;

namespace OMyFish.IdentityService.Application.Interfaces;

public interface ITokenService
{
    string CreateAccessToken(User user);

    string CreateRefreshToken(User user);

    /// Null if the token doesn't verify, has expired, or isn't a refresh token.
    Guid? ValidateRefreshToken(string token);
}
