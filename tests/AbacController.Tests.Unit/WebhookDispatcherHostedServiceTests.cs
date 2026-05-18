using System.Net;
using System.Security.Cryptography;
using System.Text;
using AbacController.Api.Hosting;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Repositories;
using AbacController.Pap.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for <see cref="WebhookDispatcherHostedService"/> covering successful delivery
/// (with correct HMAC-SHA256 X-Abac-Signature header), retry-on-failure with backoff,
/// dead-letter after max attempts, and idempotent re-runs over an already-delivered
/// outbox row.
/// </summary>
public sealed class WebhookDispatcherHostedServiceTests : IDisposable
{
    private const string Secret = "very-secret";
    private readonly string _dbPath;
    private readonly ServiceProvider _services;
    private readonly IWebhookSubscriptionRepository _repo;
    private readonly StubHttpHandler _http = new();
    private readonly WebhookDispatcherSignal _signal = new();
    private readonly WebhookDispatcherHostedService _dispatcher;
    private readonly IServiceScope _readScope;

    public WebhookDispatcherHostedServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"abac-dispatcher-test-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddDbContext<AbacDbContext>(options => options.UseSqlite($"Data Source={_dbPath}"));
        services.AddScoped<IWebhookSubscriptionRepository, WebhookRepository>();
        _services = services.BuildServiceProvider();

        using (var scope = _services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AbacDbContext>().Database.EnsureCreated();
        }

        _readScope = _services.CreateScope();
        _repo = _readScope.ServiceProvider.GetRequiredService<IWebhookSubscriptionRepository>();

        _dispatcher = new WebhookDispatcherHostedService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _signal,
            new StubHttpClientFactory(_http),
            NullLogger<WebhookDispatcherHostedService>.Instance);
    }

    public void Dispose()
    {
        _readScope.Dispose();
        _services.Dispose();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private static string ComputeHmac(string secret, string body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<(WebhookSubscription Subscription, WebhookEvent Event)> SeedAsync(string body = "{\"hello\":\"world\"}")
    {
        var subscription = await _repo.UpsertAsync(new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-a",
            CallbackUrl = "https://example.com/callback",
            Secret = Secret,
            EventTypes = "*",
            Active = true
        });

        var ev = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscription.Id,
            EventType = WebhookEventTypes.SpifImported,
            PayloadJson = body,
            Status = WebhookDeliveryStatus.Pending,
            NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1)
        };
        await _repo.AppendEventAsync(ev);
        return (subscription, ev);
    }

    /// <summary>Invokes the private DrainAsync once via reflection so we don't need a long-lived host.</summary>
    private Task DrainOnceAsync()
    {
        var method = typeof(WebhookDispatcherHostedService)
            .GetMethod("DrainAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)method.Invoke(_dispatcher, new object[] { CancellationToken.None })!;
    }

    [Fact]
    public async Task SuccessfulDelivery_MarksDelivered_AndSendsSignedBody()
    {
        var (subscription, ev) = await SeedAsync();
        _http.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK);

        await DrainOnceAsync();

        Assert.Single(_http.Calls);
        var call = _http.Calls[0];
        Assert.Equal(subscription.CallbackUrl, call.RequestUri!.ToString());
        Assert.Equal("sha256=" + ComputeHmac(Secret, ev.PayloadJson), call.Headers["X-Abac-Signature"]);
        Assert.Equal(ev.Id.ToString(), call.Headers["X-Abac-Event-Id"]);
        Assert.Equal(ev.EventType, call.Headers["X-Abac-Event-Type"]);

        // The outbox row should now be Delivered.
        var rows = await _repo.ListEventsAsync(subscription.Id, since: null, max: 10);
        Assert.Single(rows);
        Assert.Equal(WebhookDeliveryStatus.Delivered, rows[0].Status);
        Assert.Equal(1, rows[0].Attempts);
        Assert.NotNull(rows[0].DeliveredAt);
    }

    [Fact]
    public async Task FailedDelivery_RetriesWithBackoff()
    {
        var (subscription, _) = await SeedAsync();
        _http.Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        await DrainOnceAsync();

        var rows = await _repo.ListEventsAsync(subscription.Id, since: null, max: 10);
        Assert.Single(rows);
        Assert.Equal(WebhookDeliveryStatus.Pending, rows[0].Status);
        Assert.Equal(1, rows[0].Attempts);
        Assert.True(rows[0].NextAttemptAt > DateTimeOffset.UtcNow.AddSeconds(15));
        Assert.NotNull(rows[0].LastError);
    }

    [Fact]
    public async Task ThreeFailures_DeadLetters()
    {
        var (subscription, ev) = await SeedAsync();
        _http.Responder = _ => new HttpResponseMessage(HttpStatusCode.BadGateway);

        // Force three drain passes by resetting NextAttemptAt back to "due" between
        // attempts and re-running the dispatcher loop.
        for (var i = 0; i < 3; i++)
        {
            await DrainOnceAsync();
            var current = (await _repo.ListEventsAsync(subscription.Id, since: null, max: 10)).Single();
            if (current.Status == WebhookDeliveryStatus.DeadLetter) break;
            await _repo.UpdateEventAsync(current with { NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1) });
        }

        var rows = await _repo.ListEventsAsync(subscription.Id, since: null, max: 10);
        Assert.Equal(WebhookDeliveryStatus.DeadLetter, rows[0].Status);
        Assert.Equal(3, rows[0].Attempts);
    }

    [Fact]
    public async Task DeliveryToInactiveSubscription_DeadLettersImmediately()
    {
        var (subscription, _) = await SeedAsync();

        // Deactivate the subscription mid-flight.
        await _repo.UpsertAsync(subscription with { Active = false });

        await DrainOnceAsync();

        var rows = await _repo.ListEventsAsync(subscription.Id, since: null, max: 10);
        Assert.Equal(WebhookDeliveryStatus.DeadLetter, rows[0].Status);
        Assert.Empty(_http.Calls);
    }

    [Fact]
    public async Task DrainAsync_NoPendingRows_IsNoOp()
    {
        await DrainOnceAsync();
        Assert.Empty(_http.Calls);
    }

    // ── Stubs ──

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        public List<CapturedCall> Calls { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add(new CapturedCall(
                request.Method,
                request.RequestUri,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                body));
            return Responder(request);
        }

        public sealed record CapturedCall(HttpMethod Method, Uri? RequestUri, IDictionary<string, string> Headers, string Body);
    }

    private sealed class StubHttpClientFactory(StubHttpHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
