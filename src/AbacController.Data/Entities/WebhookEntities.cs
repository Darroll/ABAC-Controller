namespace AbacController.Data.Entities;

/// <summary>
/// Persisted webhook subscription. Subscribers get HMAC-signed POSTs to
/// <see cref="CallbackUrl"/> when events matching <see cref="EventTypes"/> occur.
/// </summary>
public class WebhookSubscriptionEntity
{
    /// <summary>Subscription identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant scope (null = system-wide).</summary>
    public string? TenantId { get; set; }

    /// <summary>HTTPS callback URL.</summary>
    public string CallbackUrl { get; set; } = "";

    /// <summary>Current HMAC signing secret.</summary>
    public string Secret { get; set; } = "";

    /// <summary>Previous HMAC secret accepted during rotation overlap (nullable).</summary>
    public string? PreviousSecret { get; set; }

    /// <summary>
    /// Comma-separated list of event types this subscription receives. "*" = all.
    /// </summary>
    public string EventTypes { get; set; } = "*";

    /// <summary>Whether the subscription is currently active.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last successful delivery, if any.</summary>
    public DateTimeOffset? LastDeliveryAt { get; set; }
}

/// <summary>
/// Outbox row representing one queued delivery to a subscription.
/// </summary>
public class WebhookEventEntity
{
    /// <summary>Event identifier (GUID; sent in the X-Abac-Event-Id header for dedupe).</summary>
    public Guid Id { get; set; }

    /// <summary>Subscription this delivery targets.</summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>Event type from WebhookEventTypes.</summary>
    public string EventType { get; set; } = "";

    /// <summary>JSON-serialized payload body.</summary>
    public string PayloadJson { get; set; } = "";

    /// <summary>Delivery status (Pending|Delivered|DeadLetter).</summary>
    public int Status { get; set; }

    /// <summary>Number of delivery attempts so far.</summary>
    public int Attempts { get; set; }

    /// <summary>Earliest time this row is eligible for delivery.</summary>
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last error message, if any.</summary>
    public string? LastError { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Delivered timestamp.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }
}
