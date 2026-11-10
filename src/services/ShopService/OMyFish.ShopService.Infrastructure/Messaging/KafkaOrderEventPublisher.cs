using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.Shared.Contracts.Events;

namespace OMyFish.ShopService.Infrastructure.Messaging;

/// Publishes OrderPlacedEvent to Kafka, keyed by OrderPlacedEvent.PartitionKey. Non-blocking:
/// Produce() hands the message to librdkafka's queue and returns, so a slow or down broker
/// never holds up the payment webhook's response — delivery failures surface in the callback.
public sealed class KafkaOrderEventPublisher(IProducer<string, string> producer, ILogger<KafkaOrderEventPublisher> logger)
    : IOrderEventPublisher
{
    public void PublishOrderPlaced(Order order)
    {
        var evt = new OrderPlacedEvent(order.Id, order.UserId, order.GuestEmail, order.TotalCents,
            order.Lines.Select(l => new OrderPlacedEvent.Line(l.ProductId, l.ProductName, l.Quantity, l.UnitPriceCents))
                .ToList());
        try
        {
            producer.Produce(OrderPlacedEvent.Topic,
                new Message<string, string> { Key = evt.PartitionKey, Value = JsonSerializer.Serialize(evt) },
                report =>
                {
                    if (report.Error.IsError)
                        logger.LogError("Failed to publish OrderPlacedEvent for order {OrderId}: {Reason}",
                            order.Id, report.Error.Reason);
                });
        }
        catch (Exception e) when (e is ProduceException<string, string> or KafkaException)
        {
            logger.LogError(e, "Failed to publish OrderPlacedEvent for order {OrderId}", order.Id);
        }
    }
}

/// Used when Kafka:BootstrapServers isn't configured (tests, a stack without Kafka).
public sealed class NoOpOrderEventPublisher : IOrderEventPublisher
{
    public void PublishOrderPlaced(Order order) { }
}
