using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OMyFish.NotificationService.Entities;
using OMyFish.NotificationService.Persistence;
using OMyFish.Shared.Contracts.Events;

namespace OMyFish.NotificationService.Consumers;

/// In-app "order confirmed" notification from shop-service's Kafka OrderPlacedEvent stream
/// (consumer group "notification-service", independent of shop-service's own ledger group).
/// Port of omyfish-java's OrderPlacedConsumer. A plain Confluent.Kafka loop rather than
/// MassTransit's Kafka rider: the producer is shop-service, which doesn't use MassTransit and
/// writes plain JSON, not a MassTransit envelope. At-least-once: the offset commits after the
/// row is saved; a redelivery is caught by the same SourceEventId dedup the RabbitMQ consumers
/// use (uq_notifications_source_event_id).
public sealed class OrderPlacedConsumer(
    string bootstrapServers, IServiceScopeFactory scopes, ILogger<OrderPlacedConsumer> logger) : BackgroundService
{
    public const string GroupId = "notification-service";

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunAsync(stoppingToken), stoppingToken);

    private async Task RunAsync(CancellationToken ct)
    {
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
                if (evt is not null) await HandleAsync(evt, ct);
                consumer.Commit(result);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                // Not committed — redelivered after a short pause.
                logger.LogError(e, "OrderPlaced consumer failed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ContinueWith(_ => { });
            }
        }
        consumer.Close();
    }

    public async Task HandleAsync(OrderPlacedEvent evt, CancellationToken ct)
    {
        if (evt.UserId is not { } userId)
        {
            // Guest orders have no account to attach an in-app notification to — emailing the
            // guest is a separate concern, out of scope here (same as omyfish-java).
            logger.LogInformation("Order {OrderId} is a guest checkout — skipping in-app notification", evt.OrderId);
            return;
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        if (await db.Notifications.AnyAsync(n => n.SourceEventId == evt.EventId, ct))
        {
            logger.LogInformation("Duplicate delivery of event {EventId} — skipping", evt.EventId);
            return;
        }

        var notification = new Notification(
            userId, "ORDER_PLACED", "Order confirmed", $"Your order {evt.OrderId} has been confirmed.", evt.EventId);
        db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Notification persisted: {NotificationId} for order {OrderId}", notification.Id, evt.OrderId);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogInformation("Concurrent duplicate delivery of event {EventId} — skipping", evt.EventId);
        }
    }
}
