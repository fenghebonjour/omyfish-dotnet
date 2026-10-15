using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using OMyFish.ShopService.Application.Interfaces;
using OMyFish.ShopService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace OMyFish.ShopService.Tests;

// Endpoint-level slice test host: a real WebApplicationFactory<Program> wired to real Postgres
// via Testcontainers — only the two genuinely-external dependencies (Stripe, MinIO) are faked.
public class ShopApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4-alpine")
        .WithDatabase("omyfish")
        .WithUsername("omyfish")
        .WithPassword("omyfish_dev")
        .Build();

    public FakePaymentGateway FakePayment { get; } = new();
    public FakeImageStorageService FakeStorage { get; } = new();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public ShopDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ShopDbContext>().UseNpgsql(PostgresConnectionString).Options);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Forces the host to build now (runs DbUp migrations) so a wiring failure surfaces at
        // fixture setup instead of inside the first test.
        _ = Services;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(FakePayment);
            services.RemoveAll<IImageStorageService>();
            services.AddSingleton<IImageStorageService>(FakeStorage);
        });
    }
}

[CollectionDefinition("ShopApi")]
public class ShopApiCollection : ICollectionFixture<ShopApiFixture>;

public sealed class FakePaymentGateway : IPaymentGateway
{
    public bool IsConfigured { get; set; } = true;
    public string NextSessionId { get; set; } = "sess_fake";
    public string NextCheckoutUrl { get; set; } = "https://stripe.test/checkout/sess_fake";

    public Task<CheckoutSession?> CreateCheckoutSessionAsync(
        Guid orderId, IReadOnlyList<PaymentLineItem> lines, string successUrl, string cancelUrl,
        string idempotencyKey, string? customerEmail, CancellationToken ct = default) =>
        Task.FromResult<CheckoutSession?>(IsConfigured ? new CheckoutSession(NextSessionId, NextCheckoutUrl) : null);

    public Task<RefundResult?> RefundAsync(
        string? paymentIntentId, long? amountCents, string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult<RefundResult?>(new RefundResult("re_fake", "succeeded", amountCents));

    public PaymentEvent? NextWebhookEvent { get; set; }

    public Task<PaymentEvent?> VerifyWebhookAsync(
        string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default) =>
        Task.FromResult(NextWebhookEvent);
}

public sealed class FakeImageStorageService : IImageStorageService
{
    public Task<string> UploadAsync(
        Stream data, long size, string? contentType, string? originalFileName, CancellationToken ct = default) =>
        Task.FromResult($"products/{Guid.NewGuid()}.jpg");

    public Task<StoredImage> DownloadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(new StoredImage(new MemoryStream([1, 2, 3]), "image/jpeg"));
}
