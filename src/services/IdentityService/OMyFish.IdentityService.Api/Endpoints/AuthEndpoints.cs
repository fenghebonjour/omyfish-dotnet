using System.Security.Claims;
using MediatR;
using OMyFish.IdentityService.Application.Commands;
using OMyFish.IdentityService.Application.Queries;

namespace OMyFish.IdentityService.Api.Endpoints;

public static class AuthEndpoints
{
    private const string RefreshCookieName = "refresh_token";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/auth/register", async (RegisterRequest req, IMediator mediator, CancellationToken ct) =>
        {
            try
            {
                var user = await mediator.Send(new RegisterCommand(req.Email, req.Password, req.DisplayName), ct);
                return Results.Created("/api/v1/auth/me", user);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (EmailAlreadyRegisteredException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        app.MapPost("/api/v1/auth/login", async (
            LoginRequest req, HttpContext ctx, IMediator mediator, IHostEnvironment env, CancellationToken ct) =>
        {
            try
            {
                var result = await mediator.Send(new LoginCommand(req.Email, req.Password), ct);
                if (result is null) return Results.Unauthorized();

                SetRefreshCookie(ctx, result.RefreshToken, env.IsProduction());
                return Results.Ok(new TokenResponse(result.AccessToken, result.UserId, result.Email, result.Role));
            }
            catch (InactiveAccountException)
            {
                return Results.Forbid();
            }
        });

        // Refresh token now travels only as an httpOnly cookie, never in the JSON body/localStorage
        // — a stolen 30-day token via XSS was a long-lived account takeover
        // (BACKLOG.md item F, WEAKNESS_AUDIT.md §1.3).
        app.MapPost("/api/v1/auth/refresh", async (
            HttpContext ctx, IMediator mediator, IHostEnvironment env, CancellationToken ct) =>
        {
            if (!ctx.Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken) || refreshToken is null)
                return Results.Unauthorized();

            var result = await mediator.Send(new RefreshTokenCommand(refreshToken), ct);
            if (result is null) return Results.Unauthorized();

            SetRefreshCookie(ctx, result.RefreshToken, env.IsProduction());
            return Results.Ok(new TokenResponse(result.AccessToken, result.UserId, result.Email, result.Role));
        });

        app.MapPost("/api/v1/auth/logout", (HttpContext ctx) =>
        {
            ctx.Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = "/api/v1/auth" });
            return Results.Ok();
        });

        app.MapGet("/api/v1/auth/me", async (ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
        {
            var idStr = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(idStr, out var id)) return Results.Unauthorized();
            var user = await mediator.Send(new GetCurrentUserQuery(id), ct);
            return user is null ? Results.NotFound() : Results.Ok(user);
        }).RequireAuthorization();

        app.MapPost("/api/v1/users/{userId:guid}/api-keys", async (
            Guid userId, ApiKeyRequest req, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new CreateApiKeyCommand(userId, req.Name), ct);
            return result is null
                ? Results.NotFound()
                : Results.Created($"/api/v1/users/{userId}/api-keys/{result.KeyId}",
                    new ApiKeyResponse(result.KeyId, result.PlainKey, result.Name));
        }).RequireAuthorization();
    }

    private static void SetRefreshCookie(HttpContext ctx, string token, bool secure) =>
        ctx.Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            Path = "/api/v1/auth",
        });

    private record RegisterRequest(string Email, string Password, string? DisplayName);
    private record LoginRequest(string Email, string Password);
    private record TokenResponse(string Token, Guid UserId, string Email, string Role);
    private record ApiKeyRequest(string Name);
    private record ApiKeyResponse(Guid KeyId, string PlainKey, string Name);
}
