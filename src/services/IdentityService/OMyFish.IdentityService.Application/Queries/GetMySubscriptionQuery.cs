using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Queries;

public sealed record GetMySubscriptionQuery(Guid UserId) : IQuery<SubscriptionDto>;

public sealed record SubscriptionDto(
    string Status, string? Plan, DateTime? TrialEnd, DateTime? CurrentPeriodEnd, string? PaymentProcessor);

internal sealed class GetMySubscriptionQueryHandler : IQueryHandler<GetMySubscriptionQuery, SubscriptionDto>
{
    private readonly BillingService _billing;

    public GetMySubscriptionQueryHandler(BillingService billing) => _billing = billing;

    public async Task<SubscriptionDto> Handle(GetMySubscriptionQuery query, CancellationToken ct)
    {
        var sub = await _billing.MySubscriptionAsync(query.UserId, ct);
        return new SubscriptionDto(sub.EffectiveStatus, sub.Plan, sub.TrialEnd, sub.CurrentPeriodEnd, sub.PaymentProcessor);
    }
}
