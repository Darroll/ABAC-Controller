using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Policy;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Entities;
using AbacController.Pap;
using AbacController.Pdp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Api.Controllers;

/// <summary>
/// Exposes PAP administration endpoints for policy sets, policies, versions, SPIFs, and policy-change audit history.
/// </summary>
[ApiController]
[Route("pap/api")]
public sealed class PapAdminController : ControllerBase
{
    private readonly IPolicyRepository _policyRepository;
    private readonly ISpifParser _spifParser;
    private readonly ISpifRegistry _spifRegistry;
    private readonly AbacDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;
    private readonly ITenantContext _tenantContext;
    private readonly IWebhookPublisher _webhookPublisher;

    /// <summary>Initializes a new instance of the <see cref="PapAdminController"/> class.</summary>
    public PapAdminController(
        IPolicyRepository policyRepository,
        ISpifParser spifParser,
        ISpifRegistry spifRegistry,
        AbacDbContext dbContext,
        IAuditWriter auditWriter,
        ITenantContext tenantContext,
        IWebhookPublisher webhookPublisher)
    {
        _policyRepository = policyRepository;
        _spifParser = spifParser;
        _spifRegistry = spifRegistry;
        _dbContext = dbContext;
        _auditWriter = auditWriter;
        _tenantContext = tenantContext;
        _webhookPublisher = webhookPublisher;
    }

    /// <summary>Extracts the actor identity from the current JWT claims.</summary>
    private string GetActorIdentity()
        => User.FindFirst("sub")?.Value
           ?? User.FindFirst("client_id")?.Value
           ?? "anonymous";

    /// <summary>Records a policy change audit event.</summary>
    private void AuditPolicyChange(string action, string resourceType, string resourceId, object? detail = null)
    {
        _auditWriter.Write(new AuditEvent
        {
            EventType = "policy_change",
            ActionName = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ActorIdentity = GetActorIdentity(),
            DetailJson = detail is not null ? JsonSerializer.Serialize(detail) : null,
            TenantId = _tenantContext.TenantId
        });
    }

    /// <summary>Lists all policy sets visible to the current tenant.</summary>
    [HttpGet("policy-sets")]
    [Authorize(Policy = "PolicyRead")]
    public Task<List<Core.Domain.Policy.PolicySet>> ListPolicySets(CancellationToken ct)
        => _policyRepository.GetPolicySetsAsync(ct);

    /// <summary>Gets a policy set by identifier.</summary>
    [HttpGet("policy-sets/{id}")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<ActionResult<Core.Domain.Policy.PolicySet>> GetPolicySet(string id, CancellationToken ct)
    {
        var item = await _policyRepository.GetPolicySetAsync(id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>Creates a new policy set or updates an existing one.</summary>
    [HttpPut("policy-sets/{id}")]
    [Authorize(Policy = "PolicyWrite")]
    public async Task<ActionResult<Core.Domain.Policy.PolicySet>> UpsertPolicySet(string id, [FromBody] Core.Domain.Policy.PolicySet policySet, CancellationToken ct)
    {
        if (!string.Equals(id, policySet.Id, StringComparison.Ordinal))
            return BadRequest("Route id must match policySet.id");

        var existing = await _policyRepository.GetPolicySetAsync(id, ct);
        var isCreate = existing is null;
        var saved = isCreate
            ? await _policyRepository.CreatePolicySetAsync(policySet, ct)
            : await _policyRepository.UpdatePolicySetAsync(policySet, ct);

        AuditPolicyChange(
            isCreate ? "create_policy_set" : "update_policy_set",
            "policy_set", id,
            new { name = policySet.Name, combiningAlgorithm = policySet.CombiningAlgorithm, isActive = policySet.IsActive });

        return Ok(saved);
    }

    /// <summary>Deletes a policy set.</summary>
    [HttpDelete("policy-sets/{id}")]
    [Authorize(Policy = "PolicyAdmin")]
    public async Task<IActionResult> DeletePolicySet(string id, CancellationToken ct)
    {
        await _policyRepository.DeletePolicySetAsync(id, ct);
        AuditPolicyChange("delete_policy_set", "policy_set", id);
        return NoContent();
    }

    /// <summary>Gets a policy by identifier.</summary>
    [HttpGet("policies/{id}")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<ActionResult<Core.Domain.Policy.Policy>> GetPolicy(string id, CancellationToken ct)
    {
        var item = await _policyRepository.GetPolicyAsync(id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>Creates a new policy or updates an existing one.</summary>
    [HttpPut("policies/{id}")]
    [Authorize(Policy = "PolicyWrite")]
    public async Task<ActionResult<Core.Domain.Policy.Policy>> UpsertPolicy(string id, [FromBody] Core.Domain.Policy.Policy policy, CancellationToken ct)
    {
        if (!string.Equals(id, policy.Id, StringComparison.Ordinal))
            return BadRequest("Route id must match policy.id");

        var existing = await _policyRepository.GetPolicyAsync(id, ct);
        var isCreate = existing is null;
        var saved = isCreate
            ? await _policyRepository.CreatePolicyAsync(policy, ct)
            : await _policyRepository.UpdatePolicyAsync(policy, ct);

        AuditPolicyChange(
            isCreate ? "create_policy" : "update_policy",
            "policy", id,
            new { name = policy.Name, policySetId = policy.PolicySetId, format = policy.Format });

        return Ok(saved);
    }

    /// <summary>Deletes a policy.</summary>
    [HttpDelete("policies/{id}")]
    [Authorize(Policy = "PolicyAdmin")]
    public async Task<IActionResult> DeletePolicy(string id, CancellationToken ct)
    {
        await _policyRepository.DeletePolicyAsync(id, ct);
        AuditPolicyChange("delete_policy", "policy", id);
        return NoContent();
    }

    /// <summary>Lists all versions for the specified policy.</summary>
    [HttpGet("policies/{id}/versions")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<ActionResult<List<PolicyVersion>>> ListPolicyVersions(string id, CancellationToken ct)
        => Ok(await _policyRepository.GetVersionsAsync(id, ct));

    /// <summary>Request to create a new policy version.</summary>
    /// <param name="Content">Policy content.</param>
    /// <param name="CreatedBy">Identity of the creator.</param>
    /// <param name="Activate">Whether to activate the new version immediately.</param>
    public sealed record CreatePolicyVersionRequest(string Content, string? CreatedBy = null, bool Activate = true);

    /// <summary>Creates a new version for the specified policy.</summary>
    [HttpPost("policies/{id}/versions")]
    [Authorize(Policy = "PolicyWrite")]
    public async Task<ActionResult<PolicyVersion>> CreatePolicyVersion(string id, [FromBody] CreatePolicyVersionRequest request, CancellationToken ct)
    {
        var policy = await _policyRepository.GetPolicyAsync(id, ct);
        if (policy is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("content is required");

        var existingVersions = await _policyRepository.GetVersionsAsync(id, ct);
        var nextVersionNumber = existingVersions.Count == 0 ? 1 : existingVersions.Max(v => v.VersionNumber) + 1;

        var version = new PolicyVersion
        {
            PolicyId = id,
            VersionNumber = nextVersionNumber,
            Content = request.Content,
            Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Content))),
            CreatedBy = request.CreatedBy,
            IsActive = request.Activate
        };

        var saved = await _policyRepository.CreateVersionAsync(version, ct);
        if (request.Activate)
        {
            await _policyRepository.ActivateVersionAsync(id, saved.Id, ct);
        }

        AuditPolicyChange("create_policy_version", "policy_version", id,
            new { versionId = saved.Id, versionNumber = saved.VersionNumber, hash = saved.Hash, activated = request.Activate, createdBy = request.CreatedBy });

        return Ok(await _policyRepository.GetVersionAsync(saved.Id, ct));
    }

    /// <summary>Activates a specific version of a policy.</summary>
    [HttpPost("policies/{id}/versions/{versionId:guid}/activate")]
    [Authorize(Policy = "PolicyWrite")]
    public async Task<IActionResult> ActivatePolicyVersion(string id, Guid versionId, CancellationToken ct)
    {
        var policy = await _policyRepository.GetPolicyAsync(id, ct);
        if (policy is null)
            return NotFound();

        var version = await _policyRepository.GetVersionAsync(versionId, ct);
        if (version is null || !string.Equals(version.PolicyId, id, StringComparison.Ordinal))
            return NotFound();

        await _policyRepository.ActivateVersionAsync(id, versionId, ct);
        AuditPolicyChange("activate_policy_version", "policy_version", id,
            new { versionId, versionNumber = version.VersionNumber });
        await _webhookPublisher.PublishAsync(
            WebhookEventTypes.PolicyActivated,
            new { policyId = id, versionId, versionNumber = version.VersionNumber },
            _tenantContext.TenantId,
            ct);
        return NoContent();
    }

    /// <summary>Compares two versions of the same policy and returns their diff.</summary>
    [HttpGet("policies/{id}/versions/{leftVersionId:guid}/diff/{rightVersionId:guid}")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<ActionResult<PolicyDiffResult>> DiffPolicyVersions(string id, Guid leftVersionId, Guid rightVersionId, CancellationToken ct)
    {
        var left = await _policyRepository.GetVersionAsync(leftVersionId, ct);
        var right = await _policyRepository.GetVersionAsync(rightVersionId, ct);

        if (left is null || right is null || !string.Equals(left.PolicyId, id, StringComparison.Ordinal) || !string.Equals(right.PolicyId, id, StringComparison.Ordinal))
        {
            return NotFound();
        }

        return Ok(PolicyVersionDiff.Compare(left.Content, right.Content));
    }

    /// <summary>Request to rollback to a previous policy version.</summary>
    /// <param name="CreatedBy">Identity of the person performing the rollback.</param>
    /// <param name="Reason">Reason for the rollback.</param>
    public sealed record RollbackPolicyVersionRequest(string? CreatedBy = null, string? Reason = null);

    /// <summary>Rolls back a policy by creating and activating a new version from an earlier version's content.</summary>
    [HttpPost("policies/{id}/versions/{versionId:guid}/rollback")]
    [Authorize(Policy = "PolicyWrite")]
    public async Task<ActionResult<PolicyVersion>> RollbackPolicyVersion(string id, Guid versionId, [FromBody] RollbackPolicyVersionRequest? request, CancellationToken ct)
    {
        var policy = await _policyRepository.GetPolicyAsync(id, ct);
        if (policy is null)
            return NotFound();

        var targetVersion = await _policyRepository.GetVersionAsync(versionId, ct);
        if (targetVersion is null || !string.Equals(targetVersion.PolicyId, id, StringComparison.Ordinal))
            return NotFound();

        var existingVersions = await _policyRepository.GetVersionsAsync(id, ct);
        var nextVersionNumber = existingVersions.Count == 0 ? 1 : existingVersions.Max(v => v.VersionNumber) + 1;

        var rollbackVersion = new PolicyVersion
        {
            PolicyId = id,
            VersionNumber = nextVersionNumber,
            Content = targetVersion.Content,
            Hash = targetVersion.Hash,
            CreatedBy = request?.CreatedBy,
            IsActive = true
        };

        var saved = await _policyRepository.CreateVersionAsync(rollbackVersion, ct);
        await _policyRepository.ActivateVersionAsync(id, saved.Id, ct);

        AuditPolicyChange("rollback_policy_version", "policy_version", id,
            new
            {
                rolledBackFromVersionId = versionId,
                rolledBackFromVersionNumber = targetVersion.VersionNumber,
                newVersionId = saved.Id,
                newVersionNumber = saved.VersionNumber,
                request?.CreatedBy,
                request?.Reason
            });

        return Ok(await _policyRepository.GetVersionAsync(saved.Id, ct));
    }

    /// <summary>Lists all SPIF registrations for the current tenant.</summary>
    [HttpGet("spifs")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<ActionResult<List<SpifEntity>>> ListSpifs(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        var spifs = await _dbContext.Spifs.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.PolicyOid)
            .ToListAsync(ct);
        return Ok(spifs);
    }

    /// <summary>Request to import a SPIF from XML.</summary>
    /// <param name="Xml">Raw SPIF XML content.</param>
    /// <param name="Activate">Whether to activate the SPIF immediately.</param>
    /// <param name="SetAsDefault">Whether to set this as the default SPIF.</param>
    /// <param name="ImportedBy">Identity of the importer.</param>
    public sealed record SpifImportRequest(string Xml, bool Activate = true, bool SetAsDefault = false, string? ImportedBy = null);

    /// <summary>Imports, validates, registers, and persists a SPIF document.</summary>
    [HttpPost("spifs/import")]
    [Authorize(Policy = "PolicyWrite")]
    public async Task<ActionResult<object>> ImportSpif([FromBody] SpifImportRequest request, CancellationToken ct)
    {
        var parsed = _spifParser.Parse(request.Xml);
        if (!parsed.Success || parsed.Spif is null)
            return BadRequest(new { success = false, errors = parsed.Errors });

        var spifIndex = new SpifIndex(parsed.Spif);
        _spifRegistry.Register(spifIndex);
        if (request.SetAsDefault)
            _spifRegistry.SetDefault(spifIndex.PolicyOid);

        var tenantId = _tenantContext.TenantId;
        var exists = await _dbContext.Spifs.FirstOrDefaultAsync(
            x => x.PolicyOid == spifIndex.PolicyOid && x.TenantId == tenantId, ct);
        var entity = exists ?? new SpifEntity { Id = Guid.NewGuid(), PolicyOid = spifIndex.PolicyOid };
        entity.Name = spifIndex.PolicyName;
        entity.SchemaVersion = parsed.Spif.SchemaVersion ?? "unknown";
        entity.RawXml = request.Xml;
        entity.IsActive = request.Activate;
        entity.ImportedAt = DateTimeOffset.UtcNow;
        entity.ImportedBy = request.ImportedBy;
        entity.TenantId = tenantId;
        entity.ClassificationCount = parsed.Spif.Classifications.Count;
        entity.CategoryCount = parsed.Spif.CategoryTagSets.Sum(ts => ts.Tags.Sum(t => t.Categories.Count));
        entity.Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Xml)));

        if (exists is null)
            _dbContext.Spifs.Add(entity);

        await _dbContext.SaveChangesAsync(ct);

        AuditPolicyChange("import_spif", "spif", spifIndex.PolicyOid,
            new { name = spifIndex.PolicyName, schemaVersion = entity.SchemaVersion, setAsDefault = request.SetAsDefault, importedBy = request.ImportedBy });

        await _webhookPublisher.PublishAsync(
            WebhookEventTypes.SpifImported,
            new
            {
                policyOid = spifIndex.PolicyOid,
                name = spifIndex.PolicyName,
                schemaVersion = entity.SchemaVersion,
                setAsDefault = request.SetAsDefault
            },
            tenantId,
            ct);

        return Ok(new
        {
            success = true,
            policyOid = spifIndex.PolicyOid,
            name = spifIndex.PolicyName,
            schemaVersion = entity.SchemaVersion
        });
    }

    /// <summary>Deletes a SPIF registration by database identifier.</summary>
    [HttpDelete("spifs/{id:guid}")]
    [Authorize(Policy = "PolicyAdmin")]
    public async Task<IActionResult> DeleteSpif(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        var entity = await _dbContext.Spifs.FirstOrDefaultAsync(
            x => x.Id == id && x.TenantId == tenantId, ct);
        if (entity is null)
            return NotFound();

        // Remove from in-memory registry
        _spifRegistry.Remove(entity.PolicyOid);

        _dbContext.Spifs.Remove(entity);
        await _dbContext.SaveChangesAsync(ct);

        AuditPolicyChange("delete_spif", "spif", entity.PolicyOid,
            new { name = entity.Name, deletedById = id });

        await _webhookPublisher.PublishAsync(
            WebhookEventTypes.SpifDeleted,
            new { policyOid = entity.PolicyOid, name = entity.Name },
            tenantId,
            ct);

        return NoContent();
    }

    /// <summary>Exports a stored SPIF as raw XML.</summary>
    [HttpGet("spifs/{id:guid}/export")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<IActionResult> ExportSpif(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        var entity = await _dbContext.Spifs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (entity is null)
            return NotFound();

        return Content(entity.RawXml, "application/xml");
    }

    /// <summary>Request to check a candidate policy for conflicts.</summary>
    /// <param name="Content">The candidate policy content to check.</param>
    public sealed record ConflictCheckRequest(string Content);    

    /// <summary>Checks a candidate policy for conflicts with existing policy sets.</summary>
    [HttpPost("policies/check-conflicts")]
    [Authorize(Policy = "PolicyWrite")]
    public async Task<ActionResult<object>> CheckConflicts([FromBody] ConflictCheckRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("content is required");

        var policySets = await _policyRepository.GetPolicySetsAsync(ct);
        var conflicts = PolicyConflictDetector.DetectConflicts(request.Content, policySets);

        return Ok(new
        {
            hasConflicts = conflicts.Count > 0,
            conflictCount = conflicts.Count,
            conflicts
        });
    }

    /// <summary>Queries tenant-scoped policy-change audit history.</summary>
    [HttpGet("audit")]
    [Authorize(Policy = "AuditRead")]
    public async Task<ActionResult<AuditQueryResult>> GetAuditTrail(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? eventType,
        [FromQuery] string? resourceType,
        [FromQuery] string? actor,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var query = _dbContext.AuditEvents.AsNoTracking()
            .Where(e => (e.EventType == "policy_change" || e.EventType == "import_spif") && e.TenantId == tenantId);

        if (from.HasValue)
            query = query.Where(e => e.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.Timestamp <= to.Value);
        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(e => e.ActionName == eventType);
        if (!string.IsNullOrWhiteSpace(resourceType))
            query = query.Where(e => e.ResourceType == resourceType);
        if (!string.IsNullOrWhiteSpace(actor))
            query = query.Where(e => e.ActorIdentity == actor);

        var totalCount = await query.CountAsync(ct);
        var events = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuditEvent
            {
                Id = e.Id,
                Timestamp = e.Timestamp,
                EventType = e.EventType,
                ActionName = e.ActionName,
                ResourceType = e.ResourceType,
                ResourceId = e.ResourceId,
                ActorIdentity = e.ActorIdentity,
                DetailJson = e.DetailJson,
                TenantId = e.TenantId
            })
            .ToListAsync(ct);

        return Ok(new AuditQueryResult
        {
            Events = events,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }
}
