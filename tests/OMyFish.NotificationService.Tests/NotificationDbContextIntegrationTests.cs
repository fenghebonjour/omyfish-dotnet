using Microsoft.EntityFrameworkCore;
using OMyFish.NotificationService.Entities;
using Xunit;

namespace OMyFish.NotificationService.Tests;

// Exercises NotificationDbContext against a real, migrated Postgres. The existing consumer
// tests use EF Core's InMemory provider, which doesn't enforce real constraints — this
// specifically proves the unique index on source_event_id (migration 002, backing the §2.4
// idempotency fix) actually exists in the schema and is enforced at the DB layer, not just by
// the consumer's own AnyAsync check (BACKLOG.md item F, Testing/CI tier).
[Collection("Postgres")]
public class NotificationDbContextIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Add_ThenSaveChanges_RoundTripsTheNotification()
    {
        var userId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var notification = new Notification(userId, "OBSERVATION_CREATED", "Fish identified: Walleye",
            "Your observation of Walleye has been recorded.", sourceEventId);

        await using (var db = fixture.CreateDbContext())
        {
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
        }

        await using var verifyDb = fixture.CreateDbContext();
        var saved = await verifyDb.Notifications.SingleAsync(n => n.Id == notification.Id);
        Assert.Equal(userId, saved.UserId);
        Assert.Equal(sourceEventId, saved.SourceEventId);
        Assert.False(saved.IsRead);
    }

    [Fact]
    public async Task Add_DuplicateSourceEventId_ViolatesUniqueConstraint()
    {
        var sourceEventId = Guid.NewGuid();

        await using (var db = fixture.CreateDbContext())
        {
            db.Notifications.Add(new Notification(
                Guid.NewGuid(), "OBSERVATION_CREATED", "First", null, sourceEventId));
            await db.SaveChangesAsync();
        }

        await using var db2 = fixture.CreateDbContext();
        db2.Notifications.Add(new Notification(
            Guid.NewGuid(), "OBSERVATION_CREATED", "Second", null, sourceEventId));
        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }
}
