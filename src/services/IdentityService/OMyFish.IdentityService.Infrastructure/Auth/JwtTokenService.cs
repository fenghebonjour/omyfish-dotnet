using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using OMyFish.IdentityService.Application.Interfaces;
using OMyFish.IdentityService.Domain.Entities;

namespace OMyFish.IdentityService.Infrastructure.Auth;

public sealed class JwtTokenService : ITokenService
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromDays(1);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    private readonly SymmetricSecurityKey _signingKey;

    public JwtTokenService(SymmetricSecurityKey signingKey)
    {
        _signingKey = signingKey;
    }

    public string CreateAccessToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("token_type", "access"),
        };
        return WriteToken(claims, AccessTokenLifetime);
    }

    public string CreateRefreshToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim("token_type", "refresh"),
        };
        return WriteToken(claims, RefreshTokenLifetime);
    }

    public Guid? ValidateRefreshToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _signingKey,
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
            }, out _);
        }
        catch
        {
            return null;
        }

        if (principal.FindFirstValue("token_type") != "refresh")
            return null;

        return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;
    }

    private string WriteToken(Claim[] claims, TimeSpan lifetime)
    {
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
