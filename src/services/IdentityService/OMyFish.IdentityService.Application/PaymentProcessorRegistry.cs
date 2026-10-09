using OMyFish.IdentityService.Application.Interfaces;

namespace OMyFish.IdentityService.Application;

/// Picks a gateway for a new checkout (configured default, with fallback) and looks up the
/// specific gateway an already-started subscription or webhook belongs to.
public class PaymentProcessorRegistry
{
    private readonly IReadOnlyList<IPaymentGateway> _gateways;
    private readonly string _defaultName;

    public PaymentProcessorRegistry(IReadOnlyList<IPaymentGateway> gateways, string defaultName)
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

    /// Whether a gateway with this name is registered at all, configured or not.
    public bool Exists(string name) => FindByName(name) is not null;

    /// Every gateway that's actually usable right now, in priority order.
    public IReadOnlyList<IPaymentGateway> Configured() => _gateways.Where(g => g.IsConfigured).ToList();

    /// Looks up a gateway by name. Throws if it's unknown or not configured — a corrupt-state case.
    public IPaymentGateway ByName(string name)
    {
        var gateway = FindByName(name);
        if (gateway is not { IsConfigured: true })
            throw new InvalidOperationException($"Unknown or unconfigured payment processor: {name}");
        return gateway;
    }

    private IPaymentGateway? FindByName(string name) => _gateways.FirstOrDefault(g => g.Name == name);
}
