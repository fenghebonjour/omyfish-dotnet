namespace OMyFish.ShopService.Api.Endpoints;

// Admin-only, same JWT + ADMIN-role protection as AdminProductEndpoints.
public static class AdminFeatureFlagEndpoints
{
    public static void MapAdminFeatureFlagEndpoints(this RouteGroupBuilder shop)
    {
        shop.MapPost("/admin/feature-flags/checkout", (FeatureFlagRequest req, FeatureFlags flags) =>
        {
            if (req.Enabled is null) return Results.BadRequest(new { error = "enabled is required" });
            flags.CheckoutEnabled = req.Enabled.Value;
            return Results.Ok(new { enabled = flags.CheckoutEnabled });
        })
        .RequireAuthorization(policy => policy.RequireRole("ADMIN"))
        .WithName("SetCheckoutEnabled");
    }
}

public sealed record FeatureFlagRequest(bool? Enabled);
