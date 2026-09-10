using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation(
            "Notification persisted: {NotificationId} for user {UserId}",
            notification.Id, notification.UserId);
    }
}
