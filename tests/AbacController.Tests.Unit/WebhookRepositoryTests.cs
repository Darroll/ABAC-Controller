using AbacController.Core.Domain.Webhooks;
using AbacController.Data;
using AbacController.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

/// <summary>
/// CRUD and dispatcher-side tests for <see cref="WebhookRepository"/>: subscription
/// upsert, callback lookup, event-type matching, outbox append, due-row query, retry
/// status updates, and cascade delete.
/// </summary>
public sealed class WebhookRepositoryTests : IDisposable
{
    private readonly AbacDbContext _db;
    private readonly WebhookRepository _repo;

    public WebhookRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _repo = new WebhookRepository(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private static WebhookSubscription NewSubscription(string? tenant = "tenant-a", string events = "*", bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenant,
        CallbackUrl = "https://example.com/cb",
        Secret = "s3cr3t",
        EventTypes = events,
        Active = active
    };

    [Fact]
    public async Task Upsert_CreatesNewWhenIdNotPresent()
    {
        var saved = await _repo.UpsertAsync(NewSubscription());
        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal("tenant-a", saved.TenantId);
    }

    [Fact]
    public async Task Upsert_UpdatesExistingRow()
    {
        var subscription = NewSubscription();
        await _repo.UpsertAsync(subscription);
        var updated = await _repo.UpsertAsync(subscription with { CallbackUrl = "https://other.example.com/cb", Active = false });

        Assert.Equal("https://other.example.com/cb", updated.CallbackUrl);
        Assert.False(updated.Active);

        var listed = await _repo.GetAsync(subscription.Id);
        Assert.Equal("https://other.example.com/cb", listed!.CallbackUrl);
    }

    [Fact]
    public async Task FindByCallback_TenantScoped_AndIncludesSystem()
    {
        var systemRow = await _repo.UpsertAsync(NewSubscription(tenant: null) with { CallbackUrl = "https://shared.example.com/cb" });
        var tenantRow = await _repo.UpsertAsync(NewSubscription("tenant-a") with { CallbackUrl = "https://tenant-a.example.com/cb" });
        var otherRow = await _repo.UpsertAsync(NewSubscription("tenant-b") with { CallbackUrl = "https://tenant-b.example.com/cb" });

        var systemFound = await _repo.FindByCallbackAsync("tenant-a", "https://shared.example.com/cb");
        var tenantFound = await _repo.FindByCallbackAsync("tenant-a", "https://tenant-a.example.com/cb");
        var otherNotFound = await _repo.FindByCallbackAsync("tenant-a", "https://tenant-b.example.com/cb");

        Assert.NotNull(systemFound);
        Assert.NotNull(tenantFound);
        Assert.Null(otherNotFound);
    }

    [Fact]
    public async Task GetMatching_OnlyActive_AndOnlyMatchingEventType()
    {
        await _repo.UpsertAsync(NewSubscription(events: "spif.imported,assignment.changed"));
        await _repo.UpsertAsync(NewSubscription(events: "policy.activated"));
        await _repo.UpsertAsync(NewSubscription(active: false) with { EventTypes = "*" });

        var matching = await _repo.GetMatchingAsync("tenant-a", WebhookEventTypes.AssignmentChanged);
        Assert.Single(matching);
    }

    [Fact]
    public async Task GetMatching_WildcardSubscription_MatchesAnyEvent()
    {
        await _repo.UpsertAsync(NewSubscription(events: "*"));

        var matching = await _repo.GetMatchingAsync("tenant-a", WebhookEventTypes.SpifImported);
        Assert.Single(matching);
    }

    [Fact]
    public async Task AppendEvent_StoresPendingRow_GetDueReturnsIt()
    {
        var subscription = await _repo.UpsertAsync(NewSubscription());
        var ev = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscription.Id,
            EventType = WebhookEventTypes.SpifImported,
            PayloadJson = "{}",
            Status = WebhookDeliveryStatus.Pending,
            NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1)
        };
        await _repo.AppendEventAsync(ev);

        var due = await _repo.GetDueAsync(10);
        Assert.Single(due);
        Assert.Equal(ev.Id, due[0].Id);
    }

    [Fact]
    public async Task GetDue_FiltersOutFutureRetries_AndDelivered_AndDeadLetter()
    {
        var subscription = await _repo.UpsertAsync(NewSubscription());
        var future = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscription.Id,
            EventType = "spif.imported",
            PayloadJson = "{}",
            Status = WebhookDeliveryStatus.Pending,
            NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        var delivered = future with { Id = Guid.NewGuid(), Status = WebhookDeliveryStatus.Delivered, NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-30) };
        var deadLetter = future with { Id = Guid.NewGuid(), Status = WebhookDeliveryStatus.DeadLetter, NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-30) };
        var due = future with { Id = Guid.NewGuid(), Status = WebhookDeliveryStatus.Pending, NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-30) };

        await _repo.AppendEventAsync(future);
        await _repo.AppendEventAsync(delivered);
        await _repo.AppendEventAsync(deadLetter);
        await _repo.AppendEventAsync(due);

        var rows = await _repo.GetDueAsync(10);
        Assert.Single(rows);
        Assert.Equal(due.Id, rows[0].Id);
    }

    [Fact]
    public async Task UpdateEvent_AppliesNewStatusAndAttempts()
    {
        var subscription = await _repo.UpsertAsync(NewSubscription());
        var ev = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscription.Id,
            EventType = "spif.imported",
            PayloadJson = "{}",
            NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1)
        };
        await _repo.AppendEventAsync(ev);

        await _repo.UpdateEventAsync(ev with
        {
            Status = WebhookDeliveryStatus.Delivered,
            Attempts = 2,
            DeliveredAt = DateTimeOffset.UtcNow
        });

        var listed = await _repo.ListEventsAsync(subscription.Id, since: null, max: 10);
        Assert.Single(listed);
        Assert.Equal(WebhookDeliveryStatus.Delivered, listed[0].Status);
        Assert.Equal(2, listed[0].Attempts);
        Assert.NotNull(listed[0].DeliveredAt);
    }

    [Fact]
    public async Task Delete_RemovesSubscription_AndOutboxRows()
    {
        var subscription = await _repo.UpsertAsync(NewSubscription());
        await _repo.AppendEventAsync(new WebhookEvent
        {
            Id = Guid.NewGuid(), SubscriptionId = subscription.Id,
            EventType = "spif.imported", PayloadJson = "{}"
        });

        var removed = await _repo.DeleteAsync(subscription.Id);

        Assert.True(removed);
        Assert.Null(await _repo.GetAsync(subscription.Id));
        Assert.Empty(await _repo.ListEventsAsync(subscription.Id, since: null, max: 10));
    }

    [Fact]
    public async Task ListEvents_FiltersBySinceTimestamp()
    {
        var subscription = await _repo.UpsertAsync(NewSubscription());
        var older = new WebhookEvent
        {
            Id = Guid.NewGuid(), SubscriptionId = subscription.Id,
            EventType = "spif.imported", PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        var newer = older with { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        await _repo.AppendEventAsync(older);
        await _repo.AppendEventAsync(newer);

        var since = DateTimeOffset.UtcNow.AddMinutes(-5);
        var rows = await _repo.ListEventsAsync(subscription.Id, since, max: 10);

        Assert.Single(rows);
        Assert.Equal(newer.Id, rows[0].Id);
    }
}
