using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OMyFish.ShopService.Infrastructure.Persistence;
using OMyFish.Shared.Contracts.Events;

namespace OMyFish.ShopService.Infrastructure.Messaging;

/// Builds the replayable order ledger from shop-service's own OrderPlacedEvent stream — its own
/// consumer group ("shop-service-ledger"), independent of notification-service's, so a slow
/// notification write never holds up this audit write (and vice versa). Port of omyfish-java's
/// OrderEventLedgerConsumer. At-least-once: offsets commit only after the rows are saved, and a
/// redelivery dedups per (event, product) against the table's unique index.
public sealed class OrderEventLedgerConsumer(
    string bootstrapServers, IServiceScopeFactory scopes, ILogger<OrderEventLedgerConsumer> logger) : BackgroundService
{
    public const string GroupId = "shop-service-ledger";

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // Consume() blocks, so the loop gets its own thread rather than the host's startup path.
        Task.Run(() => RunAsync(stoppingToken), stoppingToken);

    private async Task RunAsync(CancellationToken ct)
    {
        await EnsureTopicAsync();

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(OrderPlacedEvent.Topic);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(ct);
                var evt = JsonSerializer.Deserialize<OrderPlacedEvent>(result.Message.Value);
                if (evt is not null) await RecordAsync(evt, ct);
                consumer.Commit(result);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                // Not committed — the same message is redelivered after a short pause.
                logger.LogError(e, "Order ledger consumer failed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ContinueWith(_ => { });
            }
        }
        consumer.Close();
    }

    public async Task RecordAsync(OrderPlacedEvent evt, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        var inserted = 0;
        foreach (var line in evt.Lines)
        {
            // Per (event, product), not per event, so a partial earlier attempt still completes.
            if (await db.OrderEventLedger.AnyAsync(e => e.EventId == evt.EventId && e.ProductId == line.ProductId, ct))
                continue;
            db.OrderEventLedger.Add(new OrderEventLedgerEntry(
                evt.EventId, evt.OrderId, line.ProductId, line.ProductName, line.Quantity, line.UnitPriceCents));
            inserted++;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation(inserted > 0
            ? "Ledger recorded {Count} new line(s) for order {OrderId}"
            : "Duplicate delivery for order {OrderId} ({Count} new lines) — skipping", inserted, evt.OrderId);
    }

    // 3 partitions: enough for the consumer-group scaling story without over-provisioning a
    // single-broker dev cluster; per-SKU ordering holds regardless (PartitionKey). Same as
    // omyfish-java's KafkaConfig NewTopic bean.
    private async Task EnsureTopicAsync()
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([
                new Confluent.Kafka.Admin.TopicSpecification
                    { Name = OrderPlacedEvent.Topic, NumPartitions = 3, ReplicationFactor = 1 },
            ]);
        }
        catch (Confluent.Kafka.Admin.CreateTopicsException e)
            when (e.Results.All(r => r.Error.Code is ErrorCode.TopicAlreadyExists or ErrorCode.NoError))
        {
        }
    }
}
