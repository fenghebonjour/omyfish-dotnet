using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.ValueObjects;
using OMyFish.ShopService.Infrastructure.Messaging;
using OMyFish.ShopService.Infrastructure.Persistence;
using OMyFish.Shared.Contracts.Events;
using Testcontainers.Kafka;
using Xunit;

namespace OMyFish.ShopService.Tests;

// Real Kafka broker + real migrated Postgres: proves KafkaOrderEventPublisher's wire format is
// what OrderEventLedgerConsumer actually reads (the same JSON notification-service consumes),
// and that a redelivered event doesn't double-write the ledger — not just that each half
// compiles against the shared contract.
[Collection("Postgres")]
public class OrderEventKafkaRoundTripTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.6.1").Build();

    public Task InitializeAsync() => _kafka.StartAsync();
    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();

    [Fact]
    public async Task PublishedOrderPlaced_IsRecordedInTheLedger_OncePerLineEvenIfRedelivered()
    {
        var services = new ServiceCollection()
            .AddDbContext<ShopDbContext>(o => o.UseNpgsql(postgres.ConnectionString))
            .BuildServiceProvider();
        var consumer = new OrderEventLedgerConsumer(
            _kafka.GetBootstrapAddress(), services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OrderEventLedgerConsumer>.Instance);
        await consumer.StartAsync(CancellationToken.None);

        var order = Order.CreatePendingPayment(Guid.NewGuid(), null,
            [new OrderLine(Guid.NewGuid(), "Lure", 1000, 2), new OrderLine(Guid.NewGuid(), "Line", 500, 1)]);
        using (var producer = new ProducerBuilder<string, string>(
                   new ProducerConfig { BootstrapServers = _kafka.GetBootstrapAddress() }).Build())
        {
            new KafkaOrderEventPublisher(producer, NullLogger<KafkaOrderEventPublisher>.Instance).PublishOrderPlaced(order);
            producer.Flush(TimeSpan.FromSeconds(10));
        }

        var deadline = DateTime.UtcNow.AddSeconds(60);
        List<OrderEventLedgerEntry> rows = [];
        while (DateTime.UtcNow < deadline)
        {
            await using var db = postgres.CreateDbContext();
            rows = await db.OrderEventLedger.AsNoTracking().Where(e => e.OrderId == order.Id).ToListAsync();
            if (rows.Count == 2) break;
            await Task.Delay(500);
        }
        await consumer.StopAsync(CancellationToken.None);
        Assert.Equal(2, rows.Count);

        // At-least-once redelivery of the same event must not add rows.
        var redelivered = new OrderPlacedEvent(rows[0].EventId, DateTime.UtcNow, order.Id, order.UserId, null,
            order.TotalCents, order.Lines.Select(l => new OrderPlacedEvent.Line(
                l.ProductId, l.ProductName, l.Quantity, l.UnitPriceCents)).ToList());
        await consumer.RecordAsync(redelivered, CancellationToken.None);

        await using var verifyDb = postgres.CreateDbContext();
        Assert.Equal(2, await verifyDb.OrderEventLedger.CountAsync(e => e.OrderId == order.Id));
    }
}
