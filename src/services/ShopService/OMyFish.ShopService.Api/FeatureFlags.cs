namespace OMyFish.ShopService.Api;

/// In-memory checkout kill switch — lets an admin pause checkout instantly without a redeploy
/// (e.g. a processor incident mid flash-sale). Per-instance and reset on restart, same as
/// omyfish-java's FeatureFlags; a multi-replica deployment would need a shared store.
public sealed class FeatureFlags
{
    private volatile bool _checkoutEnabled = true;

    public bool CheckoutEnabled
    {
        get => _checkoutEnabled;
        set => _checkoutEnabled = value;
    }
}
