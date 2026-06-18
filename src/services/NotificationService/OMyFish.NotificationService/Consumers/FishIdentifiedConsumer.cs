using MassTransit;
using Microsoft.Extensions.Logging;
using OMyFish.Shared.Contracts.Events;

namespace OMyFish.NotificationService.Consumers;

public class FishIdentifiedConsumer : IConsumer<FishIdentifiedEvent>
{
    private readonly ILogger<FishIdentifiedConsumer> _logger;

    public FishIdentifiedConsumer(ILogger<FishIdentifiedConsumer> logger)
        => _logger = logger;

    public Task Consume(ConsumeContext<FishIdentifiedEvent> context)
    {
        var evt = context.Message;
        _logger.LogInformation(
            "Fish identified: {Species} ({Confidence:P0}) for user {UserId} — prediction {PredictionId}",
            evt.TopSpeciesName, evt.TopConfidence, evt.UserId, evt.PredictionId);

        // TODO: Push web notification / email when notification channel is added
        return Task.CompletedTask;
    }
}
