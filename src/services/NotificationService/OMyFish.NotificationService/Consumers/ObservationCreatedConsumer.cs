using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using OMyFish.NotificationService.Entities;
using OMyFish.NotificationService.Persistence;
using OMyFish.Shared.Contracts.Events;

namespace OMyFish.NotificationService.Consumers;

public class ObservationCreatedConsumer : IConsumer<ObservationCreatedEvent>
{
    private readonly NotificationDbContext _db;
    private readonly ILogger<ObservationCreatedConsumer> _logger;

    public ObservationCreatedConsumer(NotificationDbContext db, ILogger<ObservationCreatedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ObservationCreatedEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation(
            "Observation created: {ObservationId} — {Species} by user {UserId} at {ObservedAt}",
            evt.ObservationId, evt.SpeciesName, evt.UserId, evt.ObservedAt);

        // Broker redelivery (retry, at-least-once delivery) must not create a duplicate
        // notification — dedupe by the publisher's MessageId before inserting
        // (BACKLOG.md item F, WEAKNESS_AUDIT.md §2.4).
        //
        // The AnyAsync check below is only a fast path, not the guarantee: two concurrent
        // redeliveries of the same message can both pass it before either commits (TOCTOU),
        // so the uq_notifications_source_event_id constraint is the real dedup mechanism —
        // a unique-violation on SaveChangesAsync means someone else already inserted this
        // message's notification, which is exactly the safe no-op we want.
        var messageId = context.MessageId ?? Guid.NewGuid();
        if (await _db.Notifications.AnyAsync(n => n.SourceEventId == messageId, context.CancellationToken))
        {
            _logger.LogInformation("Duplicate delivery of message {MessageId} — skipping", messageId);
            return;
        }

        var notification = new Notification(
            evt.UserId,
            "OBSERVATION_CREATED",
            $"Fish identified: {evt.SpeciesName}",
            $"Your observation of {evt.SpeciesName} has been recorded.",
            messageId);
        _db.Notifications.Add(notification);

        try
        {
            await _db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _logger.LogInformation("Concurrent duplicate delivery of message {MessageId} — skipping", messageId);
            _db.Entry(notification).State = EntityState.Detached;
        }

        _logger.LogInformation(
            "Notification persisted: {NotificationId} for user {UserId}",
            notification.Id, notification.UserId);
    }
}
