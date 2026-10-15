using OMyFish.ShopService.Domain.Exceptions;

namespace OMyFish.ShopService.Api;

// The .NET-idiomatic equivalent of Java shop-service's single @RestControllerAdvice —
// applied once to the whole "/api/v1/shop" group in Program.cs instead of per endpoint.
internal sealed class ShopExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        try
        {
            return await next(ctx);
        }
        catch (Exception e) when (e is ProductNotFoundException or OrderNotFoundException)
        {
            return Results.NotFound(new { error = e.Message });
        }
        catch (CartEmptyException e)
        {
            return Results.BadRequest(new { error = e.Message });
        }
        catch (Exception e) when (e is InsufficientStockException or InvalidOrderStateException)
        {
            return Results.Conflict(new { error = e.Message });
        }
        catch (InvalidOperationException e)
        {
            // CheckoutCommandHandler throws this when no payment processor is configured, or
            // Stripe checkout-session creation itself failed.
            return Results.Problem(detail: e.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
