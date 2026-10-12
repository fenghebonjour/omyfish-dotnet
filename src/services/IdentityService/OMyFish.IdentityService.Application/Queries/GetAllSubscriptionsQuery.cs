using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Queries;

public sealed record GetAllSubscriptionsQuery : IQuery<IReadOnlyList<AdminSubscriptionRow>>;

public sealed record AdminSubscriptionRow(
    Guid UserId, string Email, string Status, string? Plan,
    DateTime? TrialEnd, DateTime? CurrentPeriodEnd, string? PaymentProcessor);

internal sealed class GetAllSubscriptionsQueryHandler
    : IQueryHandler<GetAllSubscriptionsQuery, IReadOnlyList<AdminSubscriptionRow>>
{
    private readonly BillingService _billing;

    public GetAllSubscriptionsQueryHandler(BillingService billing) => _billing = billing;

    public async Task<IReadOnlyList<AdminSubscriptionRow>> Handle(
        GetAllSubscriptionsQuery query, CancellationToken ct)
    {
        var all = await _billing.AllSubscriptionsAsync(ct);
        var emails = await _billing.UserEmailsAsync(ct);
        return all.Select(s => new AdminSubscriptionRow(
            s.UserId, emails.GetValueOrDefault(s.UserId, "?"), s.EffectiveStatus, s.Plan,
            s.TrialEnd, s.CurrentPeriodEnd, s.PaymentProcessor)).ToList();
    }
}
