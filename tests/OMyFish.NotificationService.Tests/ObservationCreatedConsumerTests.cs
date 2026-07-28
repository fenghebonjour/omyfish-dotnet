using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OMyFish.NotificationService.Consumers;
using OMyFish.NotificationService.Persistence;
using OMyFish.Shared.Contracts.Events;
using Xunit;

namespace OMyFish.NotificationService.Tests;

public class ObservationCreatedConsumerTests
{
    private static NotificationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ConsumeContext<ObservationCreatedEvent> FakeContext(ObservationCreatedEvent evt)
    {
        var ctx = Substitute.For<ConsumeContext<ObservationCreatedEvent>>();
        ctx.Message.Returns(evt);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task Consume_PersistsNotificationForTheObservationsOwner()
    {
        using var db = NewDb();
        var consumer = new ObservationCreatedConsumer(db, Substitute.For<ILogger<ObservationCreatedConsumer>>());
        var userId = Guid.NewGuid();
        var evt = new ObservationCreatedEvent(
            Guid.NewGuid(), userId, "Walleye", 45.5, -73.5, "stored/fish.jpg", DateTime.UtcNow);

        await consumer.Consume(FakeContext(evt));

        var notification = Assert.Single(db.Notifications);
        Assert.Equal(userId, notification.UserId);
        Assert.Equal("OBSERVATION_CREATED", notification.Type);
        Assert.Contains("Walleye", notification.Title);
        Assert.False(notification.IsRead);
    }
}
