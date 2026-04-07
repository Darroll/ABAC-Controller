using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Repositories;
using AbacController.Pap.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for <see cref="WebhookPublisher"/>: outbox row creation per matching
/// subscription, signal channel notification, no-op when no subscriptions match,
/// and tenant scoping of the matching query.
/// </summary>
public sealed class WebhookPublisherTests : IDisposable
{
    private readonly string _dbPath;
    private readonly WebhookRepository _repo;
    private readonly WebhookDispatcherSignal _signal;
    private readonly WebhookPublisher _publisher;
    private readonly ServiceProvider _services;
    private readonly IServiceScope _readScope;
    private readonly AbacDbContext _db;

    public WebhookPublisherTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"abac-publisher-test-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddDbContext<AbacDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"));
        services.AddScoped<IWebhookSubscriptionRepository, WebhookRepository>();
        _services = services.BuildServiceProvider();

        // Create the schema once.
        using (var scope = _services.CreateScope())
        {
            var initDb = scope.ServiceProvider.GetRequiredService<AbacDbContext>();
            initDb.Database.EnsureCreated();
        }

        _readScope = _services.CreateScope();
        _db = _readScope.ServiceProvider.GetRequiredService<AbacDbContext>();
        _repo = new WebhookRepository(_db);
        _signal = new WebhookDispatcherSignal();
        _publisher = new WebhookPublisher(_services.GetRequiredService<IServiceScopeFactory>(), _signal);
    }

    public void Dispose()
    {
        _readScope.Dispose();
        _services.Dispose();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Publish_NoMatchingSubscriptions_DoesNothing()
    {
        await _publisher.PublishAsync(WebhookEventTypes.SpifImported, new { policyOid = "x" }, "tenant-a");
        var rows = await _repo.GetDueAsync(10);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task Publish_AppendsOneRowPerMatchingSubscription()
    {
        await _repo.UpsertAsync(new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-a",
            CallbackUrl = "https://a.example.com",
            Secret = "s1",
            EventTypes = "spif.imported",
            Active = true
        });
        await _repo.UpsertAsync(new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-a",
            CallbackUrl = "https://b.example.com",
            Secret = "s2",
            EventTypes = "*",
            Active = true
        });
        // Inactive subscription must NOT receive an outbox row.
        await _repo.UpsertAsync(new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-a",
            CallbackUrl = "https://c.example.com",
            Secret = "s3",
            EventTypes = "*",
            Active = false
        });

        await _publisher.PublishAsync(WebhookEventTypes.SpifImported, new { policyOid = "1.2.3.4" }, "tenant-a");

        var rows = await _repo.GetDueAsync(10);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(WebhookEventTypes.SpifImported, r.EventType));
        Assert.All(rows, r => Assert.Contains("\"policyOid\":\"1.2.3.4\"", r.PayloadJson));
    }

    [Fact]
    public async Task Publish_DropsCrossTenantRows()
    {
        await _repo.UpsertAsync(new WebhookSubscription
        {
            Id = Guid.NewGuid(), TenantId = "tenant-other", CallbackUrl = "https://o.example.com",
            Secret = "s", EventTypes = "*", Active = true
        });

        await _publisher.PublishAsync(WebhookEventTypes.AssignmentChanged, new { changed = true }, "tenant-a");

        Assert.Empty(await _repo.GetDueAsync(10));
    }

    [Fact]
    public async Task Publish_SignalsDispatcher()
    {
        await _repo.UpsertAsync(new WebhookSubscription
        {
            Id = Guid.NewGuid(), TenantId = "tenant-a", CallbackUrl = "https://x.example.com",
            Secret = "s", EventTypes = "*", Active = true
        });

        await _publisher.PublishAsync(WebhookEventTypes.PolicyActivated, new { policyId = "p" }, "tenant-a");

        // The bounded(1) channel should now have a token waiting.
        Assert.True(_signal.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Publish_SerializesEnvelopeWithEventIdAndType()
    {
        var subscription = await _repo.UpsertAsync(new WebhookSubscription
        {
            Id = Guid.NewGuid(), TenantId = "tenant-a", CallbackUrl = "https://x.example.com",
            Secret = "s", EventTypes = "*", Active = true
        });

        await _publisher.PublishAsync(WebhookEventTypes.SpifDeleted, new { policyOid = "doomed" }, "tenant-a");

        var rows = await _repo.ListEventsAsync(subscription.Id, since: null, max: 10);
        Assert.Single(rows);
        Assert.Contains("\"eventType\":\"spif.deleted\"", rows[0].PayloadJson);
        Assert.Contains("\"eventId\":", rows[0].PayloadJson);
        Assert.Contains("\"tenantId\":\"tenant-a\"", rows[0].PayloadJson);
        Assert.Contains("\"policyOid\":\"doomed\"", rows[0].PayloadJson);
    }
}
