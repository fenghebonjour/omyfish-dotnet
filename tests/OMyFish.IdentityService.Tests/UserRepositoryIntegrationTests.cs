using Microsoft.EntityFrameworkCore;
using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Infrastructure.Repositories;
using Xunit;

namespace OMyFish.IdentityService.Tests;

// Exercises UserRepository against a real, migrated Postgres — a mocked repository can't
// catch a schema/entity mismatch, and can't verify a DB-level constraint like the unique
// index on email even exists (BACKLOG.md item F, Testing/CI tier).
[Collection("Postgres")]
public class UserRepositoryIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task CreateAsync_ThenFindByEmailAsync_RoundTripsTheUser()
    {
        var email = $"test.{Guid.NewGuid():N}@example.com";
        var user = User.Create(email, "hashed-password", "Test User");

        await using (var db = fixture.CreateDbContext())
        {
            await new UserRepository(db).CreateAsync(user);
        }

        await using var verifyDb = fixture.CreateDbContext();
        var found = await new UserRepository(verifyDb).FindByEmailAsync(email);
        Assert.NotNull(found);
        Assert.Equal(user.Id, found!.Id);
        Assert.Equal("Test User", found.DisplayName);
        Assert.Equal("USER", found.Role);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_ViolatesUniqueConstraint()
    {
        var email = $"dup.{Guid.NewGuid():N}@example.com";

        await using (var db = fixture.CreateDbContext())
        {
            await new UserRepository(db).CreateAsync(User.Create(email, "hashed-password"));
        }

        await using var db2 = fixture.CreateDbContext();
        await Assert.ThrowsAsync<DbUpdateException>(
            () => new UserRepository(db2).CreateAsync(User.Create(email, "another-hash")));
    }
}
