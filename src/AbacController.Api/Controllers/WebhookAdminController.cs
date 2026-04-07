using System.Text.Json;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AbacController.Api.Controllers;

/// <summary>
/// PAP endpoints for managing webhook subscriptions. Subscribers such as the Email
/// Classification API register a callback URL here; deliveries are performed by the
/// background <see cref="Hosting.WebhookDispatcherHostedService"/>.
/// </summary>
[ApiController]
[Route("pap/api/webhooks")]
[Authorize(Policy = "WebhookAdmin")]
public sealed class WebhookAdminController : ControllerBase
{
    private readonly IWebhookSubscriptionRepository _repository;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _auditWriter;

    public WebhookAdminController(
        IWebhookSubscriptionRepository repository,
        ITenantContext tenantContext,
        IAuditWriter auditWriter)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _auditWriter = auditWriter;
    }

    /// <summary>List webhook subscriptions visible to the current tenant.</summary>
    [HttpGet]
    public async Task<List<WebhookSubscriptionView>> List(
        [FromQuery] string? callbackUrl,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(callbackUrl))
        {
            var single = await _repository.FindByCallbackAsync(_tenantContext.TenantId, callbackUrl, ct);
            return single is null ? [] : [MapView(single)];
        }

        var list = await _repository.ListAsync(_tenantContext.TenantId, ct);
        return list.Select(MapView).ToList();
    }

    /// <summary>Get a subscription by id.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WebhookSubscriptionView>> Get(Guid id, CancellationToken ct)
    {
        var sub = await _repository.GetAsync(id, ct);
        return sub is null ? NotFound(new { error = $"Webhook '{id}' not found." }) : Ok(MapView(sub));
    }

    /// <summary>Create a new subscription. The secret is returned once in plain text.</summary>
    [HttpPost]
    public async Task<ActionResult<WebhookSubscriptionCreateResponse>> Create(
        [FromBody] WebhookSubscriptionRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CallbackUrl))
            return BadRequest(new { error = "callbackUrl is required." });

        var secret = request.Secret ?? GenerateSecret();
        var subscription = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            CallbackUrl = request.CallbackUrl,
            Secret = secret,
            EventTypes = string.IsNullOrWhiteSpace(request.EventTypes) ? "*" : request.EventTypes,
            Active = request.Active ?? true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var created = await _repository.UpsertAsync(subscription, ct);
        AuditChange("create_webhook", created.Id.ToString(), new { request.CallbackUrl, request.EventTypes });

        return Ok(new WebhookSubscriptionCreateResponse
        {
            Subscription = MapView(created),
            Secret = secret
        });
    }

    /// <summary>Delete a subscription and its outstanding outbox rows.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var removed = await _repository.DeleteAsync(id, ct);
        if (!removed) return NotFound(new { error = $"Webhook '{id}' not found." });

        AuditChange("delete_webhook", id.ToString());
        return NoContent();
    }

    /// <summary>
    /// Replay endpoint — returns every outbox event this subscription has produced
    /// since the optional <paramref name="since"/> timestamp. Subscribers that missed
    /// events while offline can use this to catch up.
    /// </summary>
    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<List<WebhookEventView>>> ListEvents(
        Guid id,
        [FromQuery] DateTimeOffset? since,
        [FromQuery] int max = 200,
        CancellationToken ct = default)
    {
        var sub = await _repository.GetAsync(id, ct);
        if (sub is null) return NotFound(new { error = $"Webhook '{id}' not found." });

        var events = await _repository.ListEventsAsync(id, since, Math.Clamp(max, 1, 1000), ct);
        return Ok(events.Select(MapEventView).ToList());
    }

    private static string GenerateSecret()
    {
        Span<byte> buffer = stackalloc byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(buffer);
        return Convert.ToHexString(buffer).ToLowerInvariant();
    }

    private static WebhookSubscriptionView MapView(WebhookSubscription s) => new()
    {
        Id = s.Id,
        TenantId = s.TenantId,
        CallbackUrl = s.CallbackUrl,
        EventTypes = s.EventTypes,
        Active = s.Active,
        CreatedAt = s.CreatedAt,
        LastDeliveryAt = s.LastDeliveryAt
    };

    private static WebhookEventView MapEventView(WebhookEvent e) => new()
    {
        Id = e.Id,
        SubscriptionId = e.SubscriptionId,
        EventType = e.EventType,
        PayloadJson = e.PayloadJson,
        Status = e.Status.ToString(),
        Attempts = e.Attempts,
        NextAttemptAt = e.NextAttemptAt,
        LastError = e.LastError,
        CreatedAt = e.CreatedAt,
        DeliveredAt = e.DeliveredAt
    };

    private string GetActorIdentity()
        => User.FindFirst("sub")?.Value
           ?? User.FindFirst("client_id")?.Value
           ?? "anonymous";

    private void AuditChange(string action, string resourceId, object? detail = null)
    {
        _auditWriter.Write(new AuditEvent
        {
            EventType = "policy_change",
            ActionName = action,
            ResourceType = "webhook_subscription",
            ResourceId = resourceId,
            ActorIdentity = GetActorIdentity(),
            DetailJson = detail is not null ? JsonSerializer.Serialize(detail) : null,
            TenantId = _tenantContext.TenantId
        });
    }

    /// <summary>Request body for creating a subscription.</summary>
    public sealed class WebhookSubscriptionRequest
    {
        /// <summary>HTTPS callback URL the subscriber wants deliveries sent to.</summary>
        public string CallbackUrl { get; set; } = "";

        /// <summary>
        /// Pre-shared HMAC secret. When null, a fresh random secret is generated and
        /// returned in the response.
        /// </summary>
        public string? Secret { get; set; }

        /// <summary>Comma-separated event types to subscribe to (default: "*").</summary>
        public string? EventTypes { get; set; }

        /// <summary>Whether to activate the subscription immediately (default: true).</summary>
        public bool? Active { get; set; }
    }

    /// <summary>Response wrapping a newly created subscription plus its secret.</summary>
    public sealed class WebhookSubscriptionCreateResponse
    {
        /// <summary>The created subscription.</summary>
        public required WebhookSubscriptionView Subscription { get; init; }

        /// <summary>
        /// The signing secret in plain text. Returned once only — subsequent GETs omit
        /// the secret and the subscriber must store it securely on first receipt.
        /// </summary>
        public required string Secret { get; init; }
    }

    /// <summary>View of a subscription without the secret.</summary>
    public sealed class WebhookSubscriptionView
    {
        /// <summary>Subscription id.</summary>
        public Guid Id { get; init; }

        /// <summary>Tenant scope.</summary>
        public string? TenantId { get; init; }

        /// <summary>Callback URL.</summary>
        public required string CallbackUrl { get; init; }

        /// <summary>Event types filter.</summary>
        public required string EventTypes { get; init; }

        /// <summary>Whether deliveries are enabled.</summary>
        public bool Active { get; init; }

        /// <summary>Creation timestamp.</summary>
        public DateTimeOffset CreatedAt { get; init; }

        /// <summary>Last successful delivery time.</summary>
        public DateTimeOffset? LastDeliveryAt { get; init; }
    }

    /// <summary>View of an outbox event for the replay endpoint.</summary>
    public sealed class WebhookEventView
    {
        /// <summary>Event id.</summary>
        public Guid Id { get; init; }

        /// <summary>Owning subscription.</summary>
        public Guid SubscriptionId { get; init; }

        /// <summary>Event type.</summary>
        public required string EventType { get; init; }

        /// <summary>Raw JSON payload as sent.</summary>
        public required string PayloadJson { get; init; }

        /// <summary>Status name (Pending/Delivered/DeadLetter).</summary>
        public required string Status { get; init; }

        /// <summary>Delivery attempts made so far.</summary>
        public int Attempts { get; init; }

        /// <summary>Earliest next attempt time.</summary>
        public DateTimeOffset NextAttemptAt { get; init; }

        /// <summary>Last error message.</summary>
        public string? LastError { get; init; }

        /// <summary>Creation timestamp.</summary>
        public DateTimeOffset CreatedAt { get; init; }

        /// <summary>Delivered timestamp.</summary>
        public DateTimeOffset? DeliveredAt { get; init; }
    }
}
