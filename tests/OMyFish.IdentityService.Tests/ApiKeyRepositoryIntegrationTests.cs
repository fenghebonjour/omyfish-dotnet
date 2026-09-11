using Microsoft.EntityFrameworkCore;
using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Infrastructure.Repositories;
using Xunit;

namespace OMyFish.IdentityService.Tests;

// Exercises ApiKeyRepository against a real, migrated Postgres — specifically the two
// behaviors only the real schema enforces: the unique index on key_hash, and the
// ON DELETE CASCADE from api_keys to users (BACKLOG.md item F, Testing/CI tier).
[Collection("Postgres")]
public class ApiKeyRepositoryIntegrationTests(PostgresFixture fixture)
{
    private async Task<User> SeedUserAsync()
    {
        var user = User.Create($"apikey-owner.{Guid.NewGuid():N}@example.com", "hashed-password");
        await using var db = fixture.CreateDbContext();
        await new UserRepository(db).CreateAsync(user);
        return user;
    }

    [Fact]
    public async Task CreateAsync_PersistsApiKeyLinkedToItsOwner()
    {
        var user = await SeedUserAsync();
        var apiKey = ApiKey.Create(user.Id, keyHash: $"hash-{Guid.NewGuid():N}", name: "My Key");

        await using (var db = fixture.CreateDbContext())
        {
            await new ApiKeyRepository(db).CreateAsync(apiKey);
        }

        await using var verifyDb = fixture.CreateDbContext();
        var saved = await verifyDb.ApiKeys.SingleAsync(k => k.Id == apiKey.Id);
        Assert.Equal(user.Id, saved.UserId);
        Assert.Equal("My Key", saved.Name);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task CreateAsync_DuplicateKeyHash_ViolatesUniqueConstraint()
    {
        var user = await SeedUserAsync();
        var keyHash = $"dup-hash-{Guid.NewGuid():N}";

        await using (var db = fixture.CreateDbContext())
        {
            await new ApiKeyRepository(db).CreateAsync(ApiKey.Create(user.Id, keyHash, "First"));
        }

        await using var db2 = fixture.CreateDbContext();
        await Assert.ThrowsAsync<DbUpdateException>(
            () => new ApiKeyRepository(db2).CreateAsync(ApiKey.Create(user.Id, keyHash, "Second")));
    }

    [Fact]
    public async Task DeletingTheOwningUser_CascadesDeleteOfItsApiKeys()
    {
        var user = await SeedUserAsync();
        var apiKey = ApiKey.Create(user.Id, keyHash: $"hash-{Guid.NewGuid():N}", name: "Doomed Key");
        await using (var db = fixture.CreateDbContext())
        {
            await new ApiKeyRepository(db).CreateAsync(apiKey);
        }

        await using (var db = fixture.CreateDbContext())
        {
            var trackedUser = await db.Users.SingleAsync(u => u.Id == user.Id);
            db.Users.Remove(trackedUser);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = fixture.CreateDbContext();
        var stillExists = await verifyDb.ApiKeys.AnyAsync(k => k.Id == apiKey.Id);
        Assert.False(stillExists);
    }
}
