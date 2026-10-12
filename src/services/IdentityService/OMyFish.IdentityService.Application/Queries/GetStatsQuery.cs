using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Queries;

public sealed record GetStatsQuery : IQuery<BillingStats>;

internal sealed class GetStatsQueryHandler : IQueryHandler<GetStatsQuery, BillingStats>
{
    private readonly BillingService _billing;

    public GetStatsQueryHandler(BillingService billing) => _billing = billing;

    public Task<BillingStats> Handle(GetStatsQuery query, CancellationToken ct) => _billing.StatsAsync(ct);
}
