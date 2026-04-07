using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using AbacController.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IWebhookSubscriptionRepository"/>.
/// The outbox query (GetDueAsync) reads across tenants so the dispatcher can drain
/// every tenant in a single pass; reads for CRUD and listing are tenant-scoped.
/// </summary>
public sealed class WebhookRepository : IWebhookSubscriptionRepository
{
    private readonly AbacDbContext _db;

    public WebhookRepository(AbacDbContext db)
    {
        _db = db;
    }

    public async Task<List<WebhookSubscription>> ListAsync(string? tenantId, CancellationToken ct = default)
    {
        // SQLite cannot ORDER BY DateTimeOffset; do it client-side after the
        // tenant-scoped fetch like AuditRepository does.
        var rows = await _db.WebhookSubscriptions
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId || e.TenantId == null)
            .ToListAsync(ct);

        return rows
            .OrderBy(e => e.CreatedAt)
            .Select(Map)
            .ToList();
    }

    public async Task<WebhookSubscription?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.WebhookSubscriptions.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return row is null ? null : Map(row);
    }

    public async Task<WebhookSubscription?> FindByCallbackAsync(string? tenantId, string callbackUrl, CancellationToken ct = default)
    {
        var row = await _db.WebhookSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(e =>
                (e.TenantId == tenantId || e.TenantId == null)
                && e.CallbackUrl == callbackUrl, ct);

        return row is null ? null : Map(row);
    }

    public async Task<WebhookSubscription> UpsertAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        var existing = await _db.WebhookSubscriptions.FirstOrDefaultAsync(e => e.Id == subscription.Id, ct);
        if (existing is null)
        {
            var entity = new WebhookSubscriptionEntity
            {
                Id = subscription.Id == Guid.Empty ? Guid.NewGuid() : subscription.Id,
                TenantId = subscription.TenantId,
                CallbackUrl = subscription.CallbackUrl,
                Secret = subscription.Secret,
                PreviousSecret = subscription.PreviousSecret,
                EventTypes = subscription.EventTypes,
                Active = subscription.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                LastDeliveryAt = subscription.LastDeliveryAt
            };
            _db.WebhookSubscriptions.Add(entity);
            await _db.SaveChangesAsync(ct);
            return Map(entity);
        }

        existing.CallbackUrl = subscription.CallbackUrl;
        existing.Secret = subscription.Secret;
        existing.PreviousSecret = subscription.PreviousSecret;
        existing.EventTypes = subscription.EventTypes;
        existing.Active = subscription.Active;
        existing.LastDeliveryAt = subscription.LastDeliveryAt;
        await _db.SaveChangesAsync(ct);
        return Map(existing);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.WebhookSubscriptions.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (row is null) return false;

        var events = await _db.WebhookEvents.Where(e => e.SubscriptionId == id).ToListAsync(ct);
        _db.WebhookEvents.RemoveRange(events);
        _db.WebhookSubscriptions.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<WebhookSubscription>> GetMatchingAsync(string? tenantId, string eventType, CancellationToken ct = default)
    {
        var rows = await _db.WebhookSubscriptions
            .AsNoTracking()
            .Where(e => (e.TenantId == tenantId || e.TenantId == null) && e.Active)
            .ToListAsync(ct);

        return rows.Select(Map).Where(s => s.Matches(eventType)).ToList();
    }

    public async Task AppendEventAsync(WebhookEvent webhookEvent, CancellationToken ct = default)
    {
        _db.WebhookEvents.Add(new WebhookEventEntity
        {
            Id = webhookEvent.Id == Guid.Empty ? Guid.NewGuid() : webhookEvent.Id,
            SubscriptionId = webhookEvent.SubscriptionId,
            EventType = webhookEvent.EventType,
            PayloadJson = webhookEvent.PayloadJson,
            Status = (int)webhookEvent.Status,
            Attempts = webhookEvent.Attempts,
            NextAttemptAt = webhookEvent.NextAttemptAt,
            LastError = webhookEvent.LastError,
            CreatedAt = webhookEvent.CreatedAt,
            DeliveredAt = webhookEvent.DeliveredAt
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<WebhookEvent>> GetDueAsync(int max, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var pendingStatus = (int)WebhookDeliveryStatus.Pending;

        // SQLite stores DateTimeOffset as TEXT, so server-side ordering and filtering on
        // DateTimeOffset columns doesn't translate. Materialize the candidate set first
        // (filtered by Status which DOES translate) and order/filter client-side. The
        // pending set is bounded by the dispatcher batch size so this stays cheap.
        if (_db.Database.IsSqlite())
        {
            var candidates = await _db.WebhookEvents
                .Where(e => e.Status == pendingStatus)
                .ToListAsync(ct);

            return candidates
                .Where(e => e.NextAttemptAt <= now)
                .OrderBy(e => e.NextAttemptAt)
                .Take(max)
                .Select(MapEvent)
                .ToList();
        }

        var rows = await _db.WebhookEvents
            .Where(e => e.Status == pendingStatus && e.NextAttemptAt <= now)
            .OrderBy(e => e.NextAttemptAt)
            .Take(max)
            .ToListAsync(ct);

        return rows.Select(MapEvent).ToList();
    }

    public async Task UpdateEventAsync(WebhookEvent webhookEvent, CancellationToken ct = default)
    {
        var row = await _db.WebhookEvents.FirstOrDefaultAsync(e => e.Id == webhookEvent.Id, ct);
        if (row is null) return;

        row.Status = (int)webhookEvent.Status;
        row.Attempts = webhookEvent.Attempts;
        row.NextAttemptAt = webhookEvent.NextAttemptAt;
        row.LastError = webhookEvent.LastError;
        row.DeliveredAt = webhookEvent.DeliveredAt;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<WebhookEvent>> ListEventsAsync(Guid subscriptionId, DateTimeOffset? since, int max, CancellationToken ct = default)
    {
        // Filter by SubscriptionId server-side, then apply DateTimeOffset filter and
        // ordering client-side to sidestep the SQLite translator limitation that the
        // AuditRepository also has to work around.
        if (_db.Database.IsSqlite())
        {
            var rows = await _db.WebhookEvents
                .AsNoTracking()
                .Where(e => e.SubscriptionId == subscriptionId)
                .ToListAsync(ct);

            IEnumerable<WebhookEventEntity> filtered = rows;
            if (since.HasValue)
                filtered = filtered.Where(e => e.CreatedAt >= since.Value);

            return filtered
                .OrderBy(e => e.CreatedAt)
                .Take(max)
                .Select(MapEvent)
                .ToList();
        }

        var query = _db.WebhookEvents
            .AsNoTracking()
            .Where(e => e.SubscriptionId == subscriptionId);

        if (since.HasValue)
            query = query.Where(e => e.CreatedAt >= since.Value);

        var ordered = await query.OrderBy(e => e.CreatedAt).Take(max).ToListAsync(ct);
        return ordered.Select(MapEvent).ToList();
    }

    private static WebhookSubscription Map(WebhookSubscriptionEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        CallbackUrl = e.CallbackUrl,
        Secret = e.Secret,
        PreviousSecret = e.PreviousSecret,
        EventTypes = e.EventTypes,
        Active = e.Active,
        CreatedAt = e.CreatedAt,
        LastDeliveryAt = e.LastDeliveryAt
    };

    private static WebhookEvent MapEvent(WebhookEventEntity e) => new()
    {
        Id = e.Id,
        SubscriptionId = e.SubscriptionId,
        EventType = e.EventType,
        PayloadJson = e.PayloadJson,
        Status = (WebhookDeliveryStatus)e.Status,
        Attempts = e.Attempts,
        NextAttemptAt = e.NextAttemptAt,
        LastError = e.LastError,
        CreatedAt = e.CreatedAt,
        DeliveredAt = e.DeliveredAt
    };
}
