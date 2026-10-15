namespace OMyFish.ShopService.Domain.Entities;

public enum OrderStatus
{
    PendingPayment,
    Paid,
    Cancelled,
    ReturnRequested,
    Returned
}
