using OMyFish.IdentityService.Application.Commands;
using OMyFish.IdentityService.Domain.Interfaces;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Queries;

public sealed record GetCurrentUserQuery(Guid UserId) : IQuery<UserDto?>;

internal sealed class GetCurrentUserQueryHandler : IQueryHandler<GetCurrentUserQuery, UserDto?>
{
    private readonly IUserRepository _users;

    public GetCurrentUserQueryHandler(IUserRepository users) => _users = users;

    public async Task<UserDto?> Handle(GetCurrentUserQuery query, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(query.UserId, ct);
        return user is null ? null : new UserDto(user.Id, user.Email, user.DisplayName, user.Role);
    }
}
