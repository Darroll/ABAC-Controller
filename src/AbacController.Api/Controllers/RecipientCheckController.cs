using System.Diagnostics;
using System.Text.Json;
using AbacController.Core.Domain.Attributes;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AbacController.Api.Controllers;

/// <summary>
/// Purpose-built endpoint for clearance-based recipient checks.
/// Given a classification (policy OID + LACV) and a set of recipients, resolves each
/// recipient's security clearance via PIP and runs a hierarchy-dominance check.
/// Returns an aggregate Permit/Deny plus per-recipient breakdown so the caller
/// (e.g. Email Classification's send-gate) can explain the decision to the user.
/// </summary>
[ApiController]
[Route("pdp/api/recipients")]
public sealed class RecipientCheckController : ControllerBase
{
    private readonly IPipResolver _pipResolver;
    private readonly ISpifRegistry _spifRegistry;
    private readonly IAuditWriter _auditWriter;
    private readonly ITenantContext _tenantContext;

    public RecipientCheckController(
        IPipResolver pipResolver,
        ISpifRegistry spifRegistry,
        IAuditWriter auditWriter,
        ITenantContext tenantContext)
    {
        _pipResolver = pipResolver;
        _spifRegistry = spifRegistry;
        _auditWriter = auditWriter;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Checks whether each recipient has sufficient clearance for the given classification.
    /// </summary>
    [HttpPost("check")]
    [Authorize(Policy = "RecipientCheck")]
    public async Task<ActionResult<RecipientCheckResponse>> Check(
        [FromBody] RecipientCheckRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PolicyOid))
            return BadRequest(new { error = "policyOid is required." });
        if (request.Recipients is null || request.Recipients.Count == 0)
            return BadRequest(new { error = "at least one recipient is required." });

        var sw = Stopwatch.StartNew();

        var spifIndex = _spifRegistry.GetByPolicyOid(request.PolicyOid) ?? _spifRegistry.GetDefault();
        if (spifIndex is null)
            return NotFound(new { error = $"No SPIF registered for policy OID '{request.PolicyOid}'." });

        if (!spifIndex.TryGetHierarchy((LacvValue)request.ClassificationLacv, out var targetHierarchy))
            return BadRequest(new { error = $"Classification LACV {request.ClassificationLacv} not found in governing SPIF." });

        var results = new List<RecipientCheckResult>(request.Recipients.Count);
        var anyDeny = false;

        foreach (var recipient in request.Recipients)
        {
            if (string.IsNullOrWhiteSpace(recipient.Id))
            {
                results.Add(new RecipientCheckResult
                {
                    Id = recipient.Id ?? "(empty)",
                    Decision = "Deny",
                    Reason = "Recipient id is empty"
                });
                anyDeny = true;
                continue;
            }

            var pip = await _pipResolver.ResolveAsync(new AttributeResolutionRequest
            {
                SubjectId = recipient.Id,
                SubjectType = recipient.Type ?? "user",
                RequestedAttributes = ["securityClearance"]
            }, ct);

            var clearanceValue = pip.Values
                .FirstOrDefault(v => string.Equals(v.Name, "securityClearance", StringComparison.Ordinal));

            var clearance = clearanceValue?.Value as SecurityClearance;
            if (clearance is null)
            {
                results.Add(new RecipientCheckResult
                {
                    Id = recipient.Id,
                    Decision = "Deny",
                    Reason = "No security clearance resolved for recipient (fail-closed)",
                    ClearanceSource = null
                });
                anyDeny = true;
                continue;
            }

            // Hierarchy dominance check: max clearance hierarchy ≥ target hierarchy.
            var maxClearanceHierarchy = int.MinValue;
            foreach (var clearanceLacv in clearance.ClassificationLacvs)
            {
                if (spifIndex.TryGetHierarchy(clearanceLacv, out var h) && h > maxClearanceHierarchy)
                    maxClearanceHierarchy = h;
            }

            var permitted = maxClearanceHierarchy >= targetHierarchy;
            if (!permitted) anyDeny = true;

            results.Add(new RecipientCheckResult
            {
                Id = recipient.Id,
                Decision = permitted ? "Permit" : "Deny",
                Reason = permitted
                    ? $"Clearance hierarchy {maxClearanceHierarchy} dominates target {targetHierarchy}"
                    : $"Clearance hierarchy {maxClearanceHierarchy} is insufficient for target {targetHierarchy}",
                ClearanceSource = clearanceValue?.SourceId
            });
        }

        sw.Stop();

        _auditWriter.Write(new AuditEvent
        {
            EventType = "recipient_check",
            ActionName = "check",
            ActorIdentity = User.FindFirst("sub")?.Value ?? User.FindFirst("client_id")?.Value ?? "anonymous",
            ResourceType = "classification",
            ResourceId = $"{request.PolicyOid}:{request.ClassificationLacv}",
            Decision = anyDeny ? "Deny" : "Permit",
            TenantId = _tenantContext.TenantId,
            DetailJson = JsonSerializer.Serialize(new
            {
                recipients = request.Recipients.Count,
                permits = results.Count(r => r.Decision == "Permit"),
                denies = results.Count(r => r.Decision == "Deny")
            })
        });

        return Ok(new RecipientCheckResponse
        {
            Aggregate = anyDeny ? "Deny" : "Permit",
            Results = results,
            EvaluationMs = (int)sw.ElapsedMilliseconds
        });
    }

    // ── Request/response contracts ──────────────────────────────────────────

    /// <summary>Recipient check request body.</summary>
    public sealed class RecipientCheckRequest
    {
        /// <summary>Governing SPIF policy OID.</summary>
        public string PolicyOid { get; set; } = "";

        /// <summary>Target classification LACV the sender wants to use.</summary>
        public int ClassificationLacv { get; set; }

        /// <summary>Recipients to check.</summary>
        public List<RecipientRef> Recipients { get; set; } = [];
    }

    /// <summary>A single recipient reference.</summary>
    public sealed class RecipientRef
    {
        /// <summary>Recipient identifier (email address, UPN, object id).</summary>
        public string? Id { get; set; }

        /// <summary>Recipient type (default: "user").</summary>
        public string? Type { get; set; }
    }

    /// <summary>Recipient check response body.</summary>
    public sealed class RecipientCheckResponse
    {
        /// <summary>Aggregate decision. "Deny" if any recipient failed.</summary>
        public required string Aggregate { get; init; }

        /// <summary>Per-recipient decision breakdown.</summary>
        public required List<RecipientCheckResult> Results { get; init; }

        /// <summary>Evaluation time in milliseconds.</summary>
        public int EvaluationMs { get; init; }
    }

    /// <summary>A single per-recipient decision result.</summary>
    public sealed class RecipientCheckResult
    {
        /// <summary>Recipient identifier.</summary>
        public required string Id { get; init; }

        /// <summary>"Permit" or "Deny".</summary>
        public required string Decision { get; init; }

        /// <summary>Human-readable reason explaining the decision.</summary>
        public string? Reason { get; init; }

        /// <summary>PIP source that produced the clearance (null when unresolved).</summary>
        public string? ClearanceSource { get; init; }
    }
}
