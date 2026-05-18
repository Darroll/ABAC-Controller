using System.Text.Json;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Entitlements;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AbacController.Api.Controllers;

/// <summary>
/// PAP endpoints for managing tenant baseline, directory-group and per-user
/// entitlements. Consumed by applications such as Email Classification to centralise
/// the identity→entitlements model in ABAC Controller.
/// </summary>
[ApiController]
[Route("pap/api/entitlements")]
[Authorize(Policy = "EntitlementAdmin")]
public sealed class EntitlementAdminController : ControllerBase
{
    private readonly IEntitlementRepository _repository;
    private readonly IAuditWriter _auditWriter;
    private readonly IWebhookPublisher _webhookPublisher;

    public EntitlementAdminController(
        IEntitlementRepository repository,
        IAuditWriter auditWriter,
        IWebhookPublisher webhookPublisher)
    {
        _repository = repository;
        _auditWriter = auditWriter;
        _webhookPublisher = webhookPublisher;
    }

    private Task PublishAssignmentChangeAsync(string tenantId, string action, object detail, CancellationToken ct)
        => _webhookPublisher.PublishAsync(
            WebhookEventTypes.AssignmentChanged,
            new { tenantId, action, detail },
            tenantId,
            ct);

    // ── Baseline ────────────────────────────────────────────────────────────

    /// <summary>List the tenant's baseline entitlements.</summary>
    [HttpGet("{tenantId}/baseline")]
    public async Task<List<EntitlementGrant>> ListBaseline(string tenantId, CancellationToken ct)
        => await _repository.GetBaselineAsync(tenantId, ct);

    /// <summary>Add a baseline entitlement to the tenant.</summary>
    [HttpPost("{tenantId}/baseline")]
    public async Task<ActionResult<EntitlementGrant>> AddBaseline(
        string tenantId,
        [FromBody] EntitlementGrantRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PolicyOid))
            return BadRequest(new { error = "policyOid is required." });

        var grant = new EntitlementGrant
        {
            TenantId = tenantId,
            Scope = EntitlementScope.Baseline,
            PolicyOid = request.PolicyOid,
            ClassificationLacv = request.ClassificationLacv,
            TagSetOid = request.TagSetOid
        };

        var result = await _repository.AddBaselineAsync(grant, ct);
        AuditChange("add_baseline_entitlement", tenantId, new { request.PolicyOid, request.ClassificationLacv });
        await PublishAssignmentChangeAsync(tenantId, "add_baseline",
            new { request.PolicyOid, request.ClassificationLacv }, ct);
        return Ok(result);
    }

    /// <summary>Remove a baseline entitlement by policy+classification key.</summary>
    [HttpDelete("{tenantId}/baseline")]
    public async Task<IActionResult> RemoveBaseline(
        string tenantId,
        [FromQuery] string policyOid,
        [FromQuery] int? classificationLacv,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(policyOid))
            return BadRequest(new { error = "policyOid is required." });

        var removed = await _repository.RemoveBaselineAsync(tenantId, policyOid, classificationLacv, ct);
        if (!removed) return NotFound(new { error = "Baseline entitlement not found." });

        AuditChange("remove_baseline_entitlement", tenantId, new { policyOid, classificationLacv });
        await PublishAssignmentChangeAsync(tenantId, "remove_baseline",
            new { policyOid, classificationLacv }, ct);
        return NoContent();
    }

    // ── Group ───────────────────────────────────────────────────────────────

    /// <summary>List the entitlements attached to a directory group.</summary>
    [HttpGet("{tenantId}/groups/{groupId}")]
    public async Task<List<EntitlementGrant>> ListGroup(string tenantId, string groupId, CancellationToken ct)
        => await _repository.GetGroupEntitlementsAsync(tenantId, groupId, ct);

    /// <summary>Grant an entitlement to a directory group.</summary>
    [HttpPost("{tenantId}/groups/{groupId}")]
    public async Task<ActionResult<EntitlementGrant>> AddGroup(
        string tenantId,
        string groupId,
        [FromBody] EntitlementGrantRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PolicyOid))
            return BadRequest(new { error = "policyOid is required." });

        var grant = new EntitlementGrant
        {
            TenantId = tenantId,
            Scope = EntitlementScope.Group,
            TargetId = groupId,
            PolicyOid = request.PolicyOid,
            ClassificationLacv = request.ClassificationLacv,
            TagSetOid = request.TagSetOid
        };

        var result = await _repository.AddGroupEntitlementAsync(grant, ct);
        AuditChange("add_group_entitlement", tenantId, new { groupId, request.PolicyOid, request.ClassificationLacv });
        await PublishAssignmentChangeAsync(tenantId, "add_group",
            new { groupId, request.PolicyOid, request.ClassificationLacv }, ct);
        return Ok(result);
    }

    /// <summary>Remove a group entitlement.</summary>
    [HttpDelete("{tenantId}/groups/{groupId}")]
    public async Task<IActionResult> RemoveGroup(
        string tenantId,
        string groupId,
        [FromQuery] string policyOid,
        [FromQuery] int? classificationLacv,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(policyOid))
            return BadRequest(new { error = "policyOid is required." });

        var removed = await _repository.RemoveGroupEntitlementAsync(tenantId, groupId, policyOid, classificationLacv, ct);
        if (!removed) return NotFound(new { error = "Group entitlement not found." });

        AuditChange("remove_group_entitlement", tenantId, new { groupId, policyOid, classificationLacv });
        await PublishAssignmentChangeAsync(tenantId, "remove_group",
            new { groupId, policyOid, classificationLacv }, ct);
        return NoContent();
    }

    // ── User ────────────────────────────────────────────────────────────────

    /// <summary>List per-user grants and denies.</summary>
    [HttpGet("{tenantId}/users/{userId}")]
    public async Task<UserOverridesResponse> ListUser(string tenantId, string userId, CancellationToken ct)
    {
        var (grants, denies) = await _repository.GetUserOverridesAsync(tenantId, userId, ct);
        return new UserOverridesResponse { Grants = grants, Denies = denies };
    }

    /// <summary>Grant a specific entitlement to a user.</summary>
    [HttpPost("{tenantId}/users/{userId}/grants")]
    public async Task<ActionResult<EntitlementGrant>> AddUserGrant(
        string tenantId,
        string userId,
        [FromBody] EntitlementGrantRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PolicyOid))
            return BadRequest(new { error = "policyOid is required." });

        var grant = new EntitlementGrant
        {
            TenantId = tenantId,
            Scope = EntitlementScope.User,
            TargetId = userId,
            PolicyOid = request.PolicyOid,
            ClassificationLacv = request.ClassificationLacv,
            TagSetOid = request.TagSetOid
        };

        var result = await _repository.AddUserGrantAsync(grant, ct);
        AuditChange("add_user_grant", tenantId, new { userId, request.PolicyOid, request.ClassificationLacv });
        await PublishAssignmentChangeAsync(tenantId, "add_user_grant",
            new { userId, request.PolicyOid, request.ClassificationLacv }, ct);
        return Ok(result);
    }

    /// <summary>Deny a specific entitlement for a user. Denies take precedence.</summary>
    [HttpPost("{tenantId}/users/{userId}/denies")]
    public async Task<ActionResult<EntitlementDeny>> AddUserDeny(
        string tenantId,
        string userId,
        [FromBody] EntitlementDenyRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PolicyOid))
            return BadRequest(new { error = "policyOid is required." });

        var deny = new EntitlementDeny
        {
            TenantId = tenantId,
            UserId = userId,
            PolicyOid = request.PolicyOid,
            ClassificationLacv = request.ClassificationLacv,
            TagSetOid = request.TagSetOid,
            Reason = request.Reason
        };

        var result = await _repository.AddUserDenyAsync(deny, ct);
        AuditChange("add_user_deny", tenantId, new { userId, request.PolicyOid, request.ClassificationLacv, request.Reason });
        await PublishAssignmentChangeAsync(tenantId, "add_user_deny",
            new { userId, request.PolicyOid, request.ClassificationLacv, request.Reason }, ct);
        return Ok(result);
    }

    /// <summary>Remove a user override (grant or deny) by policy+classification key.</summary>
    [HttpDelete("{tenantId}/users/{userId}")]
    public async Task<IActionResult> RemoveUser(
        string tenantId,
        string userId,
        [FromQuery] string policyOid,
        [FromQuery] int? classificationLacv,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(policyOid))
            return BadRequest(new { error = "policyOid is required." });

        var removed = await _repository.RemoveUserOverrideAsync(tenantId, userId, policyOid, classificationLacv, ct);
        if (!removed) return NotFound(new { error = "User override not found." });

        AuditChange("remove_user_override", tenantId, new { userId, policyOid, classificationLacv });
        await PublishAssignmentChangeAsync(tenantId, "remove_user_override",
            new { userId, policyOid, classificationLacv }, ct);
        return NoContent();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private string GetActorIdentity()
        => User.FindFirst("sub")?.Value
           ?? User.FindFirst("client_id")?.Value
           ?? "anonymous";

    private void AuditChange(string action, string tenantId, object? detail = null)
    {
        _auditWriter.Write(new AuditEvent
        {
            EventType = "policy_change",
            ActionName = action,
            ResourceType = "entitlement",
            ResourceId = tenantId,
            ActorIdentity = GetActorIdentity(),
            DetailJson = detail is not null ? JsonSerializer.Serialize(detail) : null,
            TenantId = tenantId
        });
    }

    /// <summary>Request body for adding a grant.</summary>
    public sealed class EntitlementGrantRequest
    {
        /// <summary>SPIF policy OID this grant covers.</summary>
        public string PolicyOid { get; set; } = "";

        /// <summary>Classification LACV (null = whole policy).</summary>
        public int? ClassificationLacv { get; set; }

        /// <summary>Optional category tag set OID scope.</summary>
        public string? TagSetOid { get; set; }
    }

    /// <summary>Request body for adding a deny.</summary>
    public sealed class EntitlementDenyRequest
    {
        /// <summary>SPIF policy OID being denied.</summary>
        public string PolicyOid { get; set; } = "";

        /// <summary>Classification LACV being denied (null = whole policy).</summary>
        public int? ClassificationLacv { get; set; }

        /// <summary>Optional category tag set OID scope.</summary>
        public string? TagSetOid { get; set; }

        /// <summary>Optional free-text reason recorded for audit.</summary>
        public string? Reason { get; set; }
    }

    /// <summary>Response body listing a user's grants and denies.</summary>
    public sealed class UserOverridesResponse
    {
        /// <summary>User's explicit grants.</summary>
        public List<EntitlementGrant> Grants { get; set; } = [];

        /// <summary>User's explicit denies.</summary>
        public List<EntitlementDeny> Denies { get; set; } = [];
    }
}
