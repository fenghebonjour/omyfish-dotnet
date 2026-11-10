using System.Net;
using System.Text;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Infrastructure.Payments;
using Xunit;

namespace OMyFish.ShopService.Tests;

// PayPal's capture-inside-the-webhook flow, against a scripted HTTP handler rather than the
// real sandbox: the part of the gateway with actual branching (capture vs. already-captured
// recovery) that a happy-path end-to-end test wouldn't exercise.
public class PayPalCheckoutGatewayTests
{
    private const string ApprovedEvent = """
        {"id":"WH-1","event_type":"CHECKOUT.ORDER.APPROVED","resource":{"id":"PP-ORDER-1",
         "purchase_units":[{"shipping":{"name":{"full_name":"Ann Gler"},
           "address":{"address_line_1":"1 Lake Rd","admin_area_2":"Montreal","admin_area_1":"QC",
                      "postal_code":"H2X 1Y4","country_code":"CA"}}}]}}
        """;

    private const string CapturedOrder =
        """{"id":"PP-ORDER-1","purchase_units":[{"payments":{"captures":[{"id":"CAP-1"}]}}]}""";

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Common(HttpRequestMessage r) => r.RequestUri!.AbsolutePath switch
    {
        "/v1/oauth2/token" => Json("""{"access_token":"tok","token_type":"Bearer","expires_in":3600}"""),
        "/v1/notifications/verify-webhook-signature" => Json("""{"verification_status":"SUCCESS"}"""),
        _ => throw new InvalidOperationException($"unexpected {r.RequestUri}"),
    };

    private static PayPalCheckoutGateway Gateway(ScriptedHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://paypal.test") },
            "client", "secret", "webhook-id", "cad");

    [Fact]
    public async Task VerifyWebhook_OrderApproved_CapturesAndReturnsSessionCompleted()
    {
        var handler = new ScriptedHandler(r => r.RequestUri!.AbsolutePath == "/v2/checkout/orders/PP-ORDER-1/capture"
            ? Json(CapturedOrder, HttpStatusCode.Created)
            : Common(r));

        var evt = await Gateway(handler).VerifyWebhookAsync(ApprovedEvent, new Dictionary<string, string>());

        Assert.NotNull(evt);
        Assert.Equal(PaymentEventType.SessionCompleted, evt!.Type);
        Assert.Equal("PP-ORDER-1", evt.StripeSessionId);
        Assert.Equal("CAP-1", evt.StripePaymentIntentId);
        Assert.Equal("Montreal", evt.ShippingAddress!.City);
    }

    [Fact]
    public async Task VerifyWebhook_RedeliveredAfterCapture_RecoversExistingCapture()
    {
        var handler = new ScriptedHandler(r => r.RequestUri!.AbsolutePath switch
        {
            "/v2/checkout/orders/PP-ORDER-1/capture" => Json(
                """{"name":"UNPROCESSABLE_ENTITY","details":[{"issue":"ORDER_ALREADY_CAPTURED"}]}""",
                HttpStatusCode.UnprocessableEntity),
            "/v2/checkout/orders/PP-ORDER-1" => Json(CapturedOrder),
            _ => Common(r),
        });

        var evt = await Gateway(handler).VerifyWebhookAsync(ApprovedEvent, new Dictionary<string, string>());

        Assert.Equal("CAP-1", evt!.StripePaymentIntentId);
        Assert.Contains("GET /v2/checkout/orders/PP-ORDER-1", handler.Calls);
    }

    [Fact]
    public async Task VerifyWebhook_SignatureNotVerified_ReturnsNullWithoutCapturing()
    {
        var handler = new ScriptedHandler(r => r.RequestUri!.AbsolutePath == "/v1/notifications/verify-webhook-signature"
            ? Json("""{"verification_status":"FAILURE"}""")
            : Common(r));

        var evt = await Gateway(handler).VerifyWebhookAsync(ApprovedEvent, new Dictionary<string, string>());

        Assert.Null(evt);
        Assert.DoesNotContain(handler.Calls, c => c.Contains("/capture"));
    }
}
