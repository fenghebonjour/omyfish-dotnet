using OMyFish.ShopService.Application;
using MediatR;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.ShopService.Domain.Exceptions;
using OMyFish.ShopService.Domain.ValueObjects;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Commands;

internal sealed class CheckoutCommandHandler(
    ISender sender, ShopPaymentProcessorRegistry processors, IUnitOfWork unitOfWork,
    IIdempotencyKeyRepository idempotencyKeys)
    : ICommandHandler<CheckoutCommand, CheckoutResultDto>
{
    // How long an incomplete reservation sits before it's treated as orphaned (the process died
    // before completing/deleting it) rather than "still in progress" — comfortably past the
    // payment call's own worst-case time.
    private static readonly TimeSpan StaleReservationAge = TimeSpan.FromMinutes(1);

    public async Task<CheckoutResultDto> Handle(CheckoutCommand command, CancellationToken ct)
    {
        var paymentGateway = processors.DefaultProcessor()
                             ?? throw new InvalidOperationException("No payment processor is configured");

        var ownerKey = OwnerKey(command.Owner);
        var existing = await idempotencyKeys.FindAsync(command.IdempotencyKey, IdempotencyRecord.Checkout, ct);
        if (existing is not null && !IsOrphaned(existing))
            return Replay(existing, ownerKey);

        var reservation = existing is not null
            ? RequireOwner(existing, ownerKey)
            : await idempotencyKeys.ReserveAsync(command.IdempotencyKey, IdempotencyRecord.Checkout, ownerKey, ct);

        try
        {
            var result = await ReserveAndPayAsync(command, paymentGateway, ct);
            reservation.CompleteCheckout(result.OrderId, result.CheckoutUrl);
            await idempotencyKeys.SaveAsync(reservation, ct);
            return result;
        }
        catch
        {
            // Frees the key so the client can retry it — no order survived (compensated above).
            await idempotencyKeys.DeleteAsync(reservation, ct);
            throw;
        }
    }

    private async Task<CheckoutResultDto> ReserveAndPayAsync(
        CheckoutCommand command, IPaymentGateway paymentGateway, CancellationToken ct)
    {
        // Stock is decremented and the order is created in its own DB transaction (via
        // ReserveOrderCommand) BEFORE the processor call below, so the HTTP round-trip never
        // holds a DB transaction open. If the processor call then fails, CancelOrderCommand
        // compensates by restocking and cancelling the order it just created.
        var order = await sender.Send(new ReserveOrderCommand(command.Owner, command.GuestEmail), ct);

        var lineItems = order.Lines
            .Select(l => new PaymentLineItem(l.ProductName, l.UnitPriceCents, l.Quantity))
            .ToList();

        CheckoutSession? session;
        try
        {
            session = await paymentGateway.CreateCheckoutSessionAsync(
                order.Id, lineItems, command.SuccessUrl, command.CancelUrl, order.Id.ToString(),
                command.GuestEmail, ct);
        }
        catch (Exception e)
        {
            await sender.Send(new CancelOrderCommand(order.Id), ct);
            throw new InvalidOperationException($"Checkout session creation failed: {e.Message}", e);
        }

        if (session is null)
        {
            await sender.Send(new CancelOrderCommand(order.Id), ct);
            throw new InvalidOperationException("No payment processor is configured");
        }

        order.AttachCheckoutSession(session.SessionId, paymentGateway.Name);
        await unitOfWork.SaveChangesAsync(ct);

        return new CheckoutResultDto(order.Id, session.CheckoutUrl);
    }

    private static CheckoutResultDto Replay(IdempotencyRecord record, string ownerKey)
    {
        RequireOwner(record, ownerKey);
        if (!record.Completed)
            throw new IdempotencyConflictException("A checkout with this idempotency key is already in progress");
        return new CheckoutResultDto(record.OrderId!.Value, record.CheckoutUrl!, Replayed: true);
    }

    private static bool IsOrphaned(IdempotencyRecord record) =>
        !record.Completed && record.IsStale(StaleReservationAge);

    private static IdempotencyRecord RequireOwner(IdempotencyRecord record, string ownerKey) =>
        record.OwnerKey == ownerKey ? record : throw new ArgumentException("Idempotency key already used");

    private static string OwnerKey(CartOwner owner) =>
        owner.IsGuest ? $"guest:{owner.GuestToken}" : $"user:{owner.UserId}";
}

internal sealed class ConfirmPaymentWebhookCommandHandler(
    ShopPaymentProcessorRegistry processors, IOrderRepository orderRepo, ISender sender, IUnitOfWork unitOfWork,
    IOrderEventPublisher orderEvents)
    : ICommandHandler<ConfirmPaymentWebhookCommand, bool>
{
    public async Task<bool> Handle(ConfirmPaymentWebhookCommand command, CancellationToken ct)
    {
        var paymentGateway = processors.TryByName(command.Processor);
        if (paymentGateway is null) return false;

        var paymentEvent = await paymentGateway.VerifyWebhookAsync(command.Payload, command.Headers, ct);
        if (paymentEvent is null) return false;

        var order = paymentEvent.OrderId is { } orderId
            ? await orderRepo.FindByIdAsync(orderId, ct)
            : await orderRepo.FindByStripeSessionIdAsync(paymentEvent.StripeSessionId, ct);
        // A verified event from one processor must never settle an order another one owns.
        if (order is null || order.PaymentProcessor != paymentGateway.Name) return false;

        // Status guards make webhook retries idempotent — a processor may redeliver the same event.
        switch (paymentEvent.Type)
        {
            case PaymentEventType.SessionCompleted:
                if (order.Status == OrderStatus.PendingPayment)
                {
                    order.MarkPaid(paymentEvent.StripePaymentIntentId!, paymentEvent.ShippingAddress);
                    await unitOfWork.SaveChangesAsync(ct);
                    orderEvents.PublishOrderPlaced(order);
                }
                break;
            case PaymentEventType.SessionExpired:
                if (order.Status == OrderStatus.PendingPayment)
                    await sender.Send(new CancelOrderCommand(order.Id), ct);
                break;
        }

        return true;
    }
}
