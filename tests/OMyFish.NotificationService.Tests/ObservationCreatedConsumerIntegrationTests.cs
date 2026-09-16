using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OMyFish.NotificationService.Consumers;
using OMyFish.Shared.Contracts.Events;
using Xunit;

namespace OMyFish.NotificationService.Tests;

// Reproduces the TOCTOU race in Consume: two redeliveries of the same message, each on its
// own DbContext, both pass the AnyAsync fast-path check before either commits. Only real
// Postgres (via PostgresFixture) enforces uq_notifications_source_event_id and lets the
// consumer's catch around SaveChangesAsync actually be exercised — the InMemory-provider
// tests in ObservationCreatedConsumerTests can't trigger this (WEAKNESS_AUDIT.md §2.4).
[Collection("Postgres")]
public class ObservationCreatedConsumerIntegrationTests(PostgresFixture fixture)
{
    private static ConsumeContext<ObservationCreatedEvent> FakeContext(
        ObservationCreatedEvent evt, Guid messageId)
    {
        var ctx = Substitute.For<ConsumeContext<ObservationCreatedEvent>>();
        ctx.Message.Returns(evt);
        ctx.CancellationToken.Returns(CancellationToken.None);
        ctx.MessageId.Returns(messageId);
        return ctx;
    }

    [Fact]
    public async Task Consume_ConcurrentRedelivery_DoesNotThrowAndPersistsOnlyOneNotification()
    {
        var evt = new ObservationCreatedEvent(
            Guid.NewGuid(), Guid.NewGuid(), "Walleye", 45.5, -73.5, "stored/fish.jpg", DateTime.UtcNow);
        var messageId = Guid.NewGuid();

        await using var db1 = fixture.CreateDbContext();
        await using var db2 = fixture.CreateDbContext();
        var consumer1 = new ObservationCreatedConsumer(db1, Substitute.For<ILogger<ObservationCreatedConsumer>>());
        var consumer2 = new ObservationCreatedConsumer(db2, Substitute.For<ILogger<ObservationCreatedConsumer>>());

        await Task.WhenAll(
            consumer1.Consume(FakeContext(evt, messageId)),
            consumer2.Consume(FakeContext(evt, messageId)));

        await using var verifyDb = fixture.CreateDbContext();
        var notification = Assert.Single(verifyDb.Notifications.Where(n => n.SourceEventId == messageId));
        Assert.Equal(evt.UserId, notification.UserId);
    }
}
