using System.Text.Json;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Groups;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AbacController.Api.Controllers;

/// <summary>
/// PAP endpoints for managing first-class ABAC groups and their direct +
/// inherited (Keycloak) memberships. Every write emits a
/// <see cref="WebhookEventTypes.AbacGroupChanged"/> event so subscribers
/// (Email Classification's classification cache) can drop stale per-subject
/// visibility, plus a <c>policy_change</c> audit row.
/// </summary>
[ApiController]
[Route("pap/api/groups")]
[Authorize(Policy = "GroupAdmin")]
public sealed class AbacGroupAdminController : ControllerBase
{
    private readonly IAbacGroupRepository _repository;
    private readonly IGroupMembershipResolver _resolver;
    private readonly IAuditWriter _auditWriter;
    private readonly IWebhookPublisher _webhookPublisher;

    public AbacGroupAdminController(
        IAbacGroupRepository repository,
        IGroupMembershipResolver resolver,
        IAuditWriter auditWriter,
        IWebhookPublisher webhookPublisher)
    {
        _repository = repository;
        _resolver = resolver;
        _auditWriter = auditWriter;
        _webhookPublisher = webhookPublisher;
    }

    // ── Group CRUD ──────────────────────────────────────────────────────────

    /// <summary>List ABAC groups visible to the tenant (own + system-wide).</summary>
    [HttpGet("{tenantId}")]
    public async Task<List<AbacGroup>> List(string tenantId, CancellationToken ct)
        => await _repository.ListGroupsAsync(tenantId, ct);

    /// <summary>Get a single group by id.</summary>
    [HttpGet("{tenantId}/{groupId:guid}")]
    public async Task<ActionResult<AbacGroup>> Get(string tenantId, Guid groupId, CancellationToken ct)
    {
        var group = await _repository.GetGroupAsync(groupId, ct);
        return group is null
            ? NotFound(new { error = $"Group '{groupId}' not found." })
            : Ok(group);
    }

    /// <summary>Create a new ABAC group.</summary>
    [HttpPost("{tenantId}")]
    public async Task<ActionResult<AbacGroup>> Create(
        string tenantId,
        [FromBody] AbacGroupCreateRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "name is required." });

        var created = await _repository.CreateGroupAsync(new AbacGroup
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description
        }, ct);

        AuditChange("create_abac_group", tenantId, new { created.Id, created.Name });
        await PublishGroupChangedAsync(tenantId, "create_group", new { created.Id, created.Name }, ct);
        _resolver.InvalidateTenant(tenantId);
        return Ok(created);
    }

    /// <summary>Delete a group and every membership row attached to it.</summary>
    [HttpDelete("{tenantId}/{groupId:guid}")]
    public async Task<IActionResult> Delete(string tenantId, Guid groupId, CancellationToken ct)
    {
        var deleted = await _repository.DeleteGroupAsync(groupId, ct);
        if (!deleted) return NotFound(new { error = $"Group '{groupId}' not found." });

        AuditChange("delete_abac_group", tenantId, new { groupId });
        await PublishGroupChangedAsync(tenantId, "delete_group", new { groupId }, ct);
        _resolver.InvalidateTenant(tenantId);
        return NoContent();
    }

    // ── Membership CRUD ─────────────────────────────────────────────────────

    /// <summary>List the members of a group.</summary>
    [HttpGet("{tenantId}/{groupId:guid}/members")]
    public async Task<List<AbacGroupMembership>> ListMembers(string tenantId, Guid groupId, CancellationToken ct)
        => await _repository.ListMembersAsync(groupId, ct);

    /// <summary>Add a direct user member.</summary>
    [HttpPost("{tenantId}/{groupId:guid}/members/users/{userId}")]
    public async Task<ActionResult<AbacGroupMembership>> AddDirectUser(
        string tenantId, Guid groupId, string userId, CancellationToken ct)
    {
        var added = await _repository.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = groupId,
            Kind = AbacGroupMemberKind.User,
            MemberId = userId,
            TenantId = tenantId
        }, ct);

        AuditChange("add_group_user_member", tenantId, new { groupId, userId });
        await PublishGroupChangedAsync(tenantId, "add_user_member", new { groupId, userId }, ct);
        _resolver.InvalidateTenant(tenantId);
        return Ok(added);
    }

    /// <summary>Remove a direct user member.</summary>
    [HttpDelete("{tenantId}/{groupId:guid}/members/users/{userId}")]
    public async Task<IActionResult> RemoveDirectUser(
        string tenantId, Guid groupId, string userId, CancellationToken ct)
    {
        var removed = await _repository.RemoveMemberAsync(groupId, AbacGroupMemberKind.User, userId, ct);
        if (!removed) return NotFound(new { error = "Direct user member not found." });

        AuditChange("remove_group_user_member", tenantId, new { groupId, userId });
        await PublishGroupChangedAsync(tenantId, "remove_user_member", new { groupId, userId }, ct);
        _resolver.InvalidateTenant(tenantId);
        return NoContent();
    }

    /// <summary>Add an inherited Keycloak group reference.</summary>
    [HttpPost("{tenantId}/{groupId:guid}/members/keycloak-groups/{keycloakGroupId}")]
    public async Task<ActionResult<AbacGroupMembership>> AddKeycloakGroup(
        string tenantId, Guid groupId, string keycloakGroupId, CancellationToken ct)
    {
        var added = await _repository.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = groupId,
            Kind = AbacGroupMemberKind.KeycloakGroup,
            MemberId = keycloakGroupId,
            TenantId = tenantId
        }, ct);

        AuditChange("add_group_keycloak_member", tenantId, new { groupId, keycloakGroupId });
        await PublishGroupChangedAsync(tenantId, "add_keycloak_member", new { groupId, keycloakGroupId }, ct);
        _resolver.InvalidateTenant(tenantId);
        return Ok(added);
    }

    /// <summary>Remove an inherited Keycloak group reference.</summary>
    [HttpDelete("{tenantId}/{groupId:guid}/members/keycloak-groups/{keycloakGroupId}")]
    public async Task<IActionResult> RemoveKeycloakGroup(
        string tenantId, Guid groupId, string keycloakGroupId, CancellationToken ct)
    {
        var removed = await _repository.RemoveMemberAsync(
            groupId, AbacGroupMemberKind.KeycloakGroup, keycloakGroupId, ct);

        if (!removed) return NotFound(new { error = "Inherited Keycloak member not found." });

        AuditChange("remove_group_keycloak_member", tenantId, new { groupId, keycloakGroupId });
        await PublishGroupChangedAsync(tenantId, "remove_keycloak_member",
            new { groupId, keycloakGroupId }, ct);
        _resolver.InvalidateTenant(tenantId);
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
            ResourceType = "abac_group",
            ResourceId = tenantId,
            ActorIdentity = GetActorIdentity(),
            DetailJson = detail is not null ? JsonSerializer.Serialize(detail) : null,
            TenantId = tenantId
        });
    }

    private Task PublishGroupChangedAsync(string tenantId, string action, object detail, CancellationToken ct)
        => _webhookPublisher.PublishAsync(
            WebhookEventTypes.AbacGroupChanged,
            new { tenantId, action, detail },
            tenantId,
            ct);

    /// <summary>Request body for creating an ABAC group.</summary>
    public sealed class AbacGroupCreateRequest
    {
        /// <summary>Group name (required).</summary>
        public string Name { get; set; } = "";

        /// <summary>Optional description.</summary>
        public string? Description { get; set; }
    }
}
