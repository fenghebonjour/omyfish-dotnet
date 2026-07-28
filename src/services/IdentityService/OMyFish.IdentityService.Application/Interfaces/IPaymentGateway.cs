namespace OMyFish.IdentityService.Application.Interfaces;

public interface IPaymentGateway
{
    bool IsConfigured { get; }

    Task<string?> CreateCheckoutUrlAsync(
        Guid userId, string email, string plan, CancellationToken ct = default);

    /// Null when the signature doesn't verify or the payload isn't a recognized event.
    PaymentEvent? VerifyWebhook(string payload, string signature);
}

public sealed record PaymentEvent(
    string Type,
    string? ClientReferenceId,
    string? CustomerId,
    string? SubscriptionId,
    string? Plan,
    string? ProviderStatus,
    DateTime? PeriodEnd);
