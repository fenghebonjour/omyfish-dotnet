using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OMyFish.NotificationService.Consumers;
using OMyFish.NotificationService.Persistence;
using OMyFish.Shared.Contracts.Events;
using Xunit;

namespace OMyFish.NotificationService.Tests;

[Collection("Postgres")]
public class OrderPlacedConsumerIntegrationTests(PostgresFixture fixture)
{
    private OrderPlacedConsumer Consumer() => new(
        "unused:9092",
        new ServiceCollection()
            .AddDbContext<NotificationDbContext>(o => o.UseNpgsql(fixture.ConnectionString))
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>(),
        NullLogger<OrderPlacedConsumer>.Instance);

    private static OrderPlacedEvent Event(Guid? userId) =>
        new(Guid.NewGuid(), userId, userId is null ? "guest@example.com" : null, 1000,
            [new OrderPlacedEvent.Line(Guid.NewGuid(), "Lure", 1, 1000)]);

    [Fact]
    public async Task Handle_AccountOrder_PersistsOneNotificationEvenIfRedelivered()
    {
        var evt = Event(Guid.NewGuid());
        var consumer = Consumer();

        await Task.WhenAll(consumer.HandleAsync(evt, CancellationToken.None), consumer.HandleAsync(evt, CancellationToken.None));
        await consumer.HandleAsync(evt, CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        var rows = await db.Notifications.Where(n => n.SourceEventId == evt.EventId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("ORDER_PLACED", rows[0].Type);
        Assert.Equal(evt.UserId, rows[0].UserId);
    }

    [Fact]
    public async Task Handle_GuestOrder_IsSkipped()
    {
        var evt = Event(userId: null);

        await Consumer().HandleAsync(evt, CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Notifications.AnyAsync(n => n.SourceEventId == evt.EventId));
    }
}
