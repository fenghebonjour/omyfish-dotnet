using MassTransit;
using Microsoft.Extensions.Logging;
using OMyFish.Shared.Contracts.Events;

namespace OMyFish.NotificationService.Consumers;

public class ObservationCreatedConsumer : IConsumer<ObservationCreatedEvent>
{
    private readonly ILogger<ObservationCreatedConsumer> _logger;

    public ObservationCreatedConsumer(ILogger<ObservationCreatedConsumer> logger)
        => _logger = logger;

    public Task Consume(ConsumeContext<ObservationCreatedEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation(
            "Observation created: {ObservationId} — {Species} by user {UserId} at {ObservedAt}",
            evt.ObservationId, evt.SpeciesName, evt.UserId, evt.ObservedAt);

        return Task.CompletedTask;
    }
}
