namespace AbacController.Core.Domain.Webhooks;

/// <summary>
/// Event types the ABAC Controller publishes to webhook subscribers.
/// </summary>
public static class WebhookEventTypes
{
    /// <summary>A SPIF document was imported or replaced.</summary>
    public const string SpifImported = "spif.imported";

    /// <summary>A SPIF document was deleted.</summary>
    public const string SpifDeleted = "spif.deleted";

    /// <summary>A policy version was activated.</summary>
    public const string PolicyActivated = "policy.activated";

    /// <summary>A baseline/group/user entitlement grant or deny was added or removed.</summary>
    public const string AssignmentChanged = "assignment.changed";

    /// <summary>An application registration was created or updated.</summary>
    public const string ApplicationUpdated = "application.updated";

    /// <summary>
    /// An ABAC group was created/deleted, or its membership changed. Subscribers
    /// (e.g. Email Classification's classification cache) should drop any cached
    /// per-subject visibility because effective group memberships may have moved.
    /// </summary>
    public const string AbacGroupChanged = "abac_group.changed";
}

/// <summary>
/// Delivery status for a queued webhook event.
/// </summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Queued, awaiting first delivery or retry.</summary>
    Pending = 0,

    /// <summary>Delivered successfully.</summary>
    Delivered = 1,

    /// <summary>Exhausted retry budget.</summary>
    DeadLetter = 2
}

/// <summary>
/// A registered webhook subscription. Subscribers register a callback URL and
/// receive HMAC-signed POST requests for the event types they filter on.
/// </summary>
public sealed record WebhookSubscription
{
    /// <summary>Subscription identifier (GUID).</summary>
    public required Guid Id { get; init; }

    /// <summary>Tenant scope (null = system-wide).</summary>
    public string? TenantId { get; init; }

    /// <summary>HTTPS callback URL that receives deliveries.</summary>
    public required string CallbackUrl { get; init; }

    /// <summary>Current HMAC signing secret.</summary>
    public required string Secret { get; init; }

    /// <summary>Optional previous secret accepted during rotation overlap.</summary>
    public string? PreviousSecret { get; init; }

    /// <summary>
    /// Comma-separated list of event types this subscription receives.
    /// Use "*" to subscribe to every event.
    /// </summary>
    public required string EventTypes { get; init; } = "*";

    /// <summary>Whether deliveries are currently enabled.</summary>
    public bool Active { get; init; } = true;

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last successful delivery time, if any.</summary>
    public DateTimeOffset? LastDeliveryAt { get; init; }

    /// <summary>Returns true when the subscription accepts the given event type.</summary>
    public bool Matches(string eventType)
    {
        if (!Active) return false;
        if (EventTypes == "*") return true;
        return EventTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(t => t == "*" || string.Equals(t, eventType, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// A queued webhook event — the outbox row persisted when a publisher emits an event.
/// </summary>
public sealed record WebhookEvent
{
    /// <summary>Event identifier (used for deduplication by subscribers).</summary>
    public required Guid Id { get; init; }

    /// <summary>Subscription this delivery targets.</summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>Event type from <see cref="WebhookEventTypes"/>.</summary>
    public required string EventType { get; init; }

    /// <summary>JSON-serialized event payload sent to the subscriber.</summary>
    public required string PayloadJson { get; init; }

    /// <summary>Current delivery status.</summary>
    public WebhookDeliveryStatus Status { get; init; } = WebhookDeliveryStatus.Pending;

    /// <summary>Number of delivery attempts made so far.</summary>
    public int Attempts { get; init; }

    /// <summary>When this row is next eligible for delivery.</summary>
    public DateTimeOffset NextAttemptAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last error message (when Attempts &gt; 0 and not yet Delivered).</summary>
    public string? LastError { get; init; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Delivered timestamp (when Status == Delivered).</summary>
    public DateTimeOffset? DeliveredAt { get; init; }
}
