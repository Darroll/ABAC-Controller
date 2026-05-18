using AbacController.Core.Domain.Webhooks;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Publishes a webhook event to every matching subscription. Writes one outbox row per
/// subscription and signals the dispatcher so deliveries are attempted promptly.
/// Publishers should tag events with the correct <see cref="WebhookEventTypes"/> constant.
/// </summary>
public interface IWebhookPublisher
{
    /// <summary>
    /// Publish a webhook event. Payload is serialized to JSON and persisted as an outbox
    /// row for every subscription matching <paramref name="eventType"/>. The call returns
    /// once the rows are durable; actual HTTP delivery runs asynchronously in a
    /// background dispatcher.
    /// </summary>
    /// <param name="eventType">One of <see cref="WebhookEventTypes"/>.</param>
    /// <param name="payload">JSON-serializable payload.</param>
    /// <param name="tenantId">
    /// Tenant the event belongs to. Only subscriptions with matching TenantId (or null,
    /// system-wide) receive the event.
    /// </param>
    Task PublishAsync(string eventType, object payload, string? tenantId, CancellationToken ct = default);
}

/// <summary>
/// CRUD for webhook subscriptions plus dispatcher-side queries.
/// </summary>
public interface IWebhookSubscriptionRepository
{
    /// <summary>List subscriptions visible to the current tenant.</summary>
    Task<List<WebhookSubscription>> ListAsync(string? tenantId, CancellationToken ct = default);

    /// <summary>Get a subscription by id.</summary>
    Task<WebhookSubscription?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Find an existing subscription whose callback URL and tenant match. Used by
    /// idempotent "register my callback" flows such as the Email API's startup hook.
    /// </summary>
    Task<WebhookSubscription?> FindByCallbackAsync(string? tenantId, string callbackUrl, CancellationToken ct = default);

    /// <summary>Create or replace a subscription.</summary>
    Task<WebhookSubscription> UpsertAsync(WebhookSubscription subscription, CancellationToken ct = default);

    /// <summary>Delete a subscription and every outstanding outbox row.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>List active subscriptions matching an event type for publishing.</summary>
    Task<List<WebhookSubscription>> GetMatchingAsync(string? tenantId, string eventType, CancellationToken ct = default);

    /// <summary>Append a pending outbox row for a subscription.</summary>
    Task AppendEventAsync(WebhookEvent webhookEvent, CancellationToken ct = default);

    /// <summary>Return pending deliveries whose NextAttemptAt has elapsed, up to <paramref name="max"/>.</summary>
    Task<List<WebhookEvent>> GetDueAsync(int max, CancellationToken ct = default);

    /// <summary>Update an outbox row after a delivery attempt.</summary>
    Task UpdateEventAsync(WebhookEvent webhookEvent, CancellationToken ct = default);

    /// <summary>Return events for a subscription since the given timestamp (replay support).</summary>
    Task<List<WebhookEvent>> ListEventsAsync(Guid subscriptionId, DateTimeOffset? since, int max, CancellationToken ct = default);
}
