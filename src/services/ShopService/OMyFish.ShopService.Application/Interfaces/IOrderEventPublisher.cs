using OMyFish.ShopService.Domain.Entities;

namespace OMyFish.ShopService.Application.Interfaces;

public interface IOrderEventPublisher
{
    /// Fire-and-forget: the payment confirmation has already been committed by the time this
    /// runs, and the confirmation/ledger it feeds are side effects of that, not preconditions —
    /// so a publish failure is logged, never thrown. Unlike the RabbitMQ events (EF Core
    /// outbox), a crash between the commit and the publish loses the event; same tradeoff as
    /// omyfish-java's KafkaOrderEventPublisher.
    void PublishOrderPlaced(Order order);
}
