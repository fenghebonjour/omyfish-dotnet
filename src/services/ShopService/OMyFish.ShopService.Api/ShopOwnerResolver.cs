using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OMyFish.ShopService.Domain.ValueObjects;

namespace OMyFish.ShopService.Api;

// Shared by CartEndpoints and CheckoutEndpoints: both accept either an authenticated caller
// (a valid JWT — ASP.NET Core's JWT Bearer middleware still validates it even on an
// AllowAnonymous endpoint, populating HttpContext.User when a token is present) or an
// anonymous X-Cart-Token, never both.
internal static class ShopOwnerResolver
{
    public static CartOwner Resolve(HttpContext ctx, string? cartTokenHeader)
    {
        var sub = ctx.User.Identity?.IsAuthenticated == true
            ? ctx.User.FindFirst("sub")?.Value ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            : null;
        var userId = Guid.Empty;
        var guestToken = Guid.Empty;
        var hasUser = Guid.TryParse(sub, out userId);
        var hasGuest = !string.IsNullOrWhiteSpace(cartTokenHeader) && Guid.TryParse(cartTokenHeader, out guestToken);

        if (hasUser == hasGuest)
            throw new BadHttpRequestException("Provide exactly one of a valid Authorization token or X-Cart-Token");

        return hasUser ? CartOwner.OfUser(userId) : CartOwner.OfGuest(guestToken);
    }
}
