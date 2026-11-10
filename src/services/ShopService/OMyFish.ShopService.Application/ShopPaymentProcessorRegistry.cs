using OMyFish.ShopService.Application.Interfaces;

namespace OMyFish.ShopService.Application;

/// Picks a gateway for a new checkout (configured default, with fallback) and looks up the
/// specific gateway an already-started order or webhook belongs to. Same shape as
/// IdentityService's PaymentProcessorRegistry — not shared code, since the two services'
/// IPaymentGateway interfaces are deliberately separate (one-shot checkout vs. subscriptions).
public class ShopPaymentProcessorRegistry
{
    private readonly IReadOnlyList<IPaymentGateway> _gateways;
    private readonly string _defaultName;

    public ShopPaymentProcessorRegistry(IReadOnlyList<IPaymentGateway> gateways, string defaultName)
    {
        _gateways = gateways;
        _defaultName = defaultName;
    }

    /// The configured default if it's usable, else the first configured gateway in priority order.
    public IPaymentGateway? DefaultProcessor()
    {
        var preferred = FindByName(_defaultName);
        if (preferred is { IsConfigured: true }) return preferred;
        return _gateways.FirstOrDefault(g => g.IsConfigured);
    }

    /// Looks up a gateway by name. Throws if it's unknown or not configured — a corrupt-state case.
    public IPaymentGateway ByName(string name) =>
        FindByName(name) is { IsConfigured: true } gateway
            ? gateway
            : throw new InvalidOperationException($"Unknown or unconfigured payment processor: {name}");

    /// Null when unknown or not configured — for an untrusted name, like a webhook URL segment.
    public IPaymentGateway? TryByName(string name) => FindByName(name) is { IsConfigured: true } g ? g : null;

    private IPaymentGateway? FindByName(string name) => _gateways.FirstOrDefault(g => g.Name == name);
}
