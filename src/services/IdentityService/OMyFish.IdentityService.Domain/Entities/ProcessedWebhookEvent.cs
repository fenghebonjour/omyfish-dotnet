namespace OMyFish.IdentityService.Domain.Entities;

/// Dedup record for a Stripe webhook delivery — Stripe can and does redeliver the same
/// event, this records each acted-on event id so a redelivery is recognized and skipped.
public sealed class ProcessedWebhookEvent
{
    private ProcessedWebhookEvent() { }

    public string EventId { get; private set; } = "";
    public DateTime ProcessedAt { get; private set; }

    public static ProcessedWebhookEvent Of(string eventId) =>
        new() { EventId = eventId, ProcessedAt = DateTime.UtcNow };
}
