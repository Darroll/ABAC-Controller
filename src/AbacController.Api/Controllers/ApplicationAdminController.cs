using System.Text.Json;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Classifications;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AbacController.Api.Controllers;

/// <summary>
/// Exposes PAP endpoints for managing application registrations.
/// Applications define classification scope constraints for specific integrations.
/// </summary>
[ApiController]
[Route("pap/api/applications")]
public sealed class ApplicationAdminController : ControllerBase
{
    private readonly IApplicationRepository _repository;
    private readonly IAuditWriter _auditWriter;
    private readonly ITenantContext _tenantContext;
    private readonly IWebhookPublisher _webhookPublisher;

    public ApplicationAdminController(
        IApplicationRepository repository,
        IAuditWriter auditWriter,
        ITenantContext tenantContext,
        IWebhookPublisher webhookPublisher)
    {
        _repository = repository;
        _auditWriter = auditWriter;
        _tenantContext = tenantContext;
        _webhookPublisher = webhookPublisher;
    }

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
            ResourceType = "application_registration",
            ResourceId = resourceId,
            ActorIdentity = GetActorIdentity(),
            DetailJson = detail is not null ? JsonSerializer.Serialize(detail) : null,
            TenantId = _tenantContext.TenantId
        });
    }

    /// <summary>Lists all application registrations for the current tenant.</summary>
    [HttpGet]
    [Authorize(Policy = "ApplicationAdmin")]
    public async Task<List<ApplicationRegistration>> List(CancellationToken ct)
        => await _repository.GetAllAsync(ct);

    /// <summary>Gets an application registration by ID.</summary>
    [HttpGet("{id}")]
    [Authorize(Policy = "ApplicationAdmin")]
    public async Task<ActionResult<ApplicationRegistration>> Get(string id, CancellationToken ct)
    {
        var app = await _repository.GetByIdAsync(id, ct);
        return app is null ? NotFound(new { error = $"Application '{id}' not found." }) : Ok(app);
    }

    /// <summary>Creates or updates an application registration.</summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "ApplicationAdmin")]
    public async Task<ActionResult<ApplicationRegistration>> Upsert(
        string id,
        [FromBody] ApplicationRegistrationRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });

        var registration = new ApplicationRegistration
        {
            Id = id,
            Name = request.Name,
            Description = request.Description,
            DefaultPolicyOid = request.DefaultPolicyOid,
            AllowedClassificationLacvs = request.AllowedClassificationLacvs ?? [],
            MaxClassificationHierarchy = request.MaxClassificationHierarchy,
            AllowedTagSetOids = request.AllowedTagSetOids ?? [],
            IsActive = request.IsActive ?? true
        };

        var result = await _repository.UpsertAsync(registration, ct);
        AuditChange("upsert_application", id, new { request.Name, request.DefaultPolicyOid });
        await _webhookPublisher.PublishAsync(
            WebhookEventTypes.ApplicationUpdated,
            new { applicationId = id, request.Name, request.DefaultPolicyOid, isActive = result.IsActive },
            _tenantContext.TenantId,
            ct);
        return Ok(result);
    }

    /// <summary>Deletes an application registration.</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "ApplicationAdmin")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var deleted = await _repository.DeleteAsync(id, ct);
        if (!deleted)
            return NotFound(new { error = $"Application '{id}' not found." });

        AuditChange("delete_application", id);
        await _webhookPublisher.PublishAsync(
            WebhookEventTypes.ApplicationUpdated,
            new { applicationId = id, deleted = true },
            _tenantContext.TenantId,
            ct);
        return NoContent();
    }

    /// <summary>Request body for creating/updating an application registration.</summary>
    public sealed class ApplicationRegistrationRequest
    {
        /// <summary>Human-readable application name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Optional description.</summary>
        public string? Description { get; set; }

        /// <summary>Default SPIF policy OID.</summary>
        public string? DefaultPolicyOid { get; set; }

        /// <summary>Whitelist of allowed classification LACV values (empty = all).</summary>
        public List<int>? AllowedClassificationLacvs { get; set; }

        /// <summary>Maximum classification hierarchy ceiling.</summary>
        public int? MaxClassificationHierarchy { get; set; }

        /// <summary>Whitelist of allowed tag set OIDs (empty = all).</summary>
        public List<string>? AllowedTagSetOids { get; set; }

        /// <summary>Whether the registration is active (default: true).</summary>
        public bool? IsActive { get; set; }
    }
}
