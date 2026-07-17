using OMyFish.IdentityService.Domain.Entities;
using Xunit;

namespace OMyFish.IdentityService.Tests;

public class UserTests
{
    [Fact]
    public void Create_NormalizesEmail()
    {
        var user = User.Create("  Angler@Example.COM ", "hashed-pw");
        Assert.Equal("angler@example.com", user.Email);
    }

    [Fact]
    public void Create_DefaultsToActiveUserRole()
    {
        var user = User.Create("a@b.c", "hashed-pw", "Display Name");
        Assert.Equal("user", user.Role);
        Assert.True(user.IsActive);
        Assert.Equal("Display Name", user.DisplayName);
        Assert.NotEqual(Guid.Empty, user.Id);
    }

    [Fact]
    public void Deactivate_DisablesUser()
    {
        var user = User.Create("a@b.c", "hashed-pw");
        user.Deactivate();
        Assert.False(user.IsActive);
    }
}
