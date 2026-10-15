using MediatR;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Domain.Entities;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.ShopService.Application.Commands;

internal sealed class CheckoutCommandHandler(ISender sender, IPaymentGateway paymentGateway, IUnitOfWork unitOfWork)
    : ICommandHandler<CheckoutCommand, CheckoutResultDto>
{
    public async Task<CheckoutResultDto> Handle(CheckoutCommand command, CancellationToken ct)
    {
        if (!paymentGateway.IsConfigured)
            throw new InvalidOperationException("No payment processor is configured");

        // Stock is decremented and the order is created in its own DB transaction (via
        // ReserveOrderCommand) BEFORE the Stripe call below, so the Stripe HTTP round-trip
        // never holds a DB transaction open. If the Stripe call then fails, CancelOrderCommand
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

        order.AttachCheckoutSession(session.SessionId);
        await unitOfWork.SaveChangesAsync(ct);

        return new CheckoutResultDto(order.Id, session.CheckoutUrl);
    }
}

internal sealed class ConfirmPaymentWebhookCommandHandler(
    IPaymentGateway paymentGateway, IOrderRepository orderRepo, ISender sender, IUnitOfWork unitOfWork)
    : ICommandHandler<ConfirmPaymentWebhookCommand, bool>
{
    public async Task<bool> Handle(ConfirmPaymentWebhookCommand command, CancellationToken ct)
    {
        var paymentEvent = await paymentGateway.VerifyWebhookAsync(command.Payload, command.Headers, ct);
        if (paymentEvent is null) return false;

        var order = await orderRepo.FindByStripeSessionIdAsync(paymentEvent.StripeSessionId, ct);
        if (order is null) return false;

        // Status guards make webhook retries idempotent — Stripe may redeliver the same event.
        switch (paymentEvent.Type)
        {
            case PaymentEventType.SessionCompleted:
                if (order.Status == OrderStatus.PendingPayment)
                {
                    order.MarkPaid(paymentEvent.StripePaymentIntentId!, paymentEvent.ShippingAddress);
                    await unitOfWork.SaveChangesAsync(ct);
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
