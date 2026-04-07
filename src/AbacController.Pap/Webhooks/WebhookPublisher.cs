using System.Text.Json;
using System.Threading.Channels;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AbacController.Pap.Webhooks;

/// <summary>
/// Bounded signal channel used by <see cref="WebhookPublisher"/> to wake the
/// dispatcher hosted service as soon as a new outbox row is written. The channel only
/// carries "there is work to do" tokens — the actual payloads live in the database
/// so deliveries survive restarts.
/// </summary>
public sealed class WebhookDispatcherSignal
{
    private readonly Channel<byte> _channel = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });

    /// <summary>Reader used by the dispatcher.</summary>
    public ChannelReader<byte> Reader => _channel.Reader;

    /// <summary>Signals the dispatcher that a new event was enqueued.</summary>
    public void Signal() => _channel.Writer.TryWrite(0);
}

/// <summary>
/// Writes webhook events to the outbox and signals the background dispatcher. The
/// publisher resolves a scoped <see cref="IWebhookSubscriptionRepository"/> via
/// <see cref="IServiceScopeFactory"/> so it can be used from singleton callers such as
/// the audit pipeline.
/// </summary>
public sealed class WebhookPublisher : IWebhookPublisher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WebhookDispatcherSignal _signal;

    public WebhookPublisher(IServiceScopeFactory scopeFactory, WebhookDispatcherSignal signal)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
    }

    public async Task PublishAsync(string eventType, object payload, string? tenantId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWebhookSubscriptionRepository>();

        var matching = await repo.GetMatchingAsync(tenantId, eventType, ct);
        if (matching.Count == 0) return;

        var payloadJson = JsonSerializer.Serialize(new WebhookEnvelope
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            TenantId = tenantId,
            OccurredAt = DateTimeOffset.UtcNow,
            Payload = payload
        });

        foreach (var subscription in matching)
        {
            await repo.AppendEventAsync(new WebhookEvent
            {
                Id = Guid.NewGuid(),
                SubscriptionId = subscription.Id,
                EventType = eventType,
                PayloadJson = payloadJson,
                Status = WebhookDeliveryStatus.Pending,
                Attempts = 0,
                NextAttemptAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);
        }

        _signal.Signal();
    }

    /// <summary>JSON envelope sent in every webhook body.</summary>
    public sealed record WebhookEnvelope
    {
        /// <summary>Unique event id used for subscriber deduplication.</summary>
        public Guid EventId { get; init; }

        /// <summary>Event type (see <see cref="WebhookEventTypes"/>).</summary>
        public required string EventType { get; init; }

        /// <summary>Tenant the event belongs to, null for system-wide.</summary>
        public string? TenantId { get; init; }

        /// <summary>When the event happened.</summary>
        public DateTimeOffset OccurredAt { get; init; }

        /// <summary>Event-type-specific payload object.</summary>
        public required object Payload { get; init; }
    }
}
