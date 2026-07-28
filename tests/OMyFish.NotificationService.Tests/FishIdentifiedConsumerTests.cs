using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OMyFish.NotificationService.Consumers;
using OMyFish.Shared.Contracts.Events;
using Xunit;

namespace OMyFish.NotificationService.Tests;

public class FishIdentifiedConsumerTests
{
    [Fact]
    public async Task Consume_LogsAndCompletesWithoutThrowing()
    {
        var logger = Substitute.For<ILogger<FishIdentifiedConsumer>>();
        var consumer = new FishIdentifiedConsumer(logger);
        var evt = new FishIdentifiedEvent(
            Guid.NewGuid(), null, Guid.NewGuid(), "Walleye", 0.91,
            [new PredictionResult("Walleye", "Sander vitreus", 0.91, 1)], "identify/some-guid/fish.jpg");
        var ctx = Substitute.For<ConsumeContext<FishIdentifiedEvent>>();
        ctx.Message.Returns(evt);

        await consumer.Consume(ctx);

        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }
}
