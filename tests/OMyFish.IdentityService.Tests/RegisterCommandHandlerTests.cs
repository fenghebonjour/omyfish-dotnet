using NSubstitute;
using OMyFish.IdentityService.Application.Commands;
using OMyFish.IdentityService.Domain.Interfaces;
using Xunit;

namespace OMyFish.IdentityService.Tests;

public class RegisterCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ISubscriptionRepository _subscriptions = Substitute.For<ISubscriptionRepository>();

    [Theory]
    [InlineData("not-an-email", "longenough")]
    [InlineData("a@b", "longenough")]
    [InlineData("angler@example.com", "short")]
    public async Task Handle_InvalidInput_ThrowsArgumentExceptionWithoutCreatingUser(string email, string password)
    {
        var handler = new RegisterCommandHandler(_users, _subscriptions);

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.Handle(new RegisterCommand(email, password, null), CancellationToken.None));
        await _users.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ValidInput_CreatesUser()
    {
        var handler = new RegisterCommandHandler(_users, _subscriptions);

        var dto = await handler.Handle(
            new RegisterCommand("angler@example.com", "longenough", "Angler"), CancellationToken.None);

        Assert.Equal("angler@example.com", dto.Email);
        await _users.ReceivedWithAnyArgs(1).CreateAsync(default!, default);
    }
}
