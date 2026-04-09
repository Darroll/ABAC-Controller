using AbacController.Api.Auth;
using AbacController.Core.Domain.Entitlements;
using AbacController.Core.Interfaces;
using AbacController.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Api.Controllers;

/// <summary>
/// PDP endpoint that returns the set of SPIFs the calling subject is allowed
/// to see — a per-user filter on top of the tenant's full SPIF catalogue.
/// Used by Email Classification's add-in flow to populate the policy picker
/// without exposing SPIFs the user has no entitlement to.
///
/// Subject and tenant are both derived from the JWT context (or the
/// development auth handler in dev). There is no <c>{tenantId}</c> path
/// segment because handing one out would let any caller probe an arbitrary
/// tenant's SPIFs.
///
/// Visibility set:
/// <code>
/// visible = (PolicyOids ∪ ClassificationLacvsByPolicy.Keys) \ DeniedPolicyOids
/// </code>
/// — i.e. any policy with at least one entitlement grant (whole-policy or
/// classification-scoped) survives, unless a whole-policy deny removes it.
/// Classification-level denies are intentionally NOT applied here; they
/// belong to the classification query, which decides which classifications
/// inside a visible SPIF the subject can use.
/// </summary>
[ApiController]
[Route("pdp/api/spifs")]
[Authorize(Policy = "ClassificationQuery")]
public sealed class SpifVisibilityController : ControllerBase
{
    private readonly IGroupMembershipResolver _groupResolver;
    private readonly IEntitlementResolver _entitlementResolver;
    private readonly IApplicationRepository _applicationRepository;
    private readonly AbacDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IKeycloakGroupClaimsProvider _keycloakClaims;

    public SpifVisibilityController(
        IGroupMembershipResolver groupResolver,
        IEntitlementResolver entitlementResolver,
        IApplicationRepository applicationRepository,
        AbacDbContext db,
        ITenantContext tenantContext,
        IKeycloakGroupClaimsProvider keycloakClaims)
    {
        _groupResolver = groupResolver;
        _entitlementResolver = entitlementResolver;
        _applicationRepository = applicationRepository;
        _db = db;
        _tenantContext = tenantContext;
        _keycloakClaims = keycloakClaims;
    }

    /// <summary>List the SPIFs the current subject can see.</summary>
    [HttpGet("visible")]
    [HttpGet("visible/{applicationId}")]
    public async Task<ActionResult<VisibleSpifsResponse>> Visible(string? applicationId, CancellationToken ct)
    {
        // Reject missing subject / tenant explicitly instead of silently
        // falling back to "anonymous" / "default". Two different API-key
        // callers with no sub claim would otherwise share a cached
        // visibility set, and a caller with no tenant header would probe
        // the default tenant's SPIFs.
        var subjectId = User.FindFirst("sub")?.Value ?? User.FindFirst("client_id")?.Value;
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return Unauthorized(new
            {
                error = "Subject id (sub or client_id claim) is required to resolve visibility.",
            });
        }

        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return BadRequest(new
            {
                error = "Tenant id is required. Supply X-Tenant-Id or a tenant claim in the token.",
            });
        }

        var keycloakGroups = _keycloakClaims.KeycloakGroupIds;
        var abacGroupIds = await _groupResolver.ResolveAsync(tenantId, subjectId, keycloakGroups, ct);

        var entitlements = await _entitlementResolver.ResolveAsync(new EntitlementSubject
        {
            SubjectId = subjectId,
            TenantId = tenantId,
            GroupIds = abacGroupIds.Select(g => g.ToString()).ToList()
        }, ct);

        // Visibility set per the contract above. Whole-policy denies remove
        // a policy entirely; classification-level denies do not.
        var visibleOids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var oid in entitlements.PolicyOids) visibleOids.Add(oid);
        foreach (var kvp in entitlements.ClassificationLacvsByPolicy) visibleOids.Add(kvp.Key);
        foreach (var deniedOid in entitlements.DeniedPolicyOids) visibleOids.Remove(deniedOid);

        // Load metadata for the visible SPIFs from the tenant's SPIF table.
        var spifs = await _db.Spifs
            .AsNoTracking()
            .Where(s => (s.TenantId == tenantId || s.TenantId == null) && visibleOids.Contains(s.PolicyOid))
            .ToListAsync(ct);

        // Resolve the default SPIF: prefer the application registration's
        // configured default if it's in the visible set, then fall back to the
        // first visible SPIF (alphabetical) so the add-in always has a default.
        string? defaultPolicyOid = null;
        if (!string.IsNullOrWhiteSpace(applicationId))
        {
            var app = await _applicationRepository.GetByIdAsync(applicationId, ct);
            if (app is not null
                && !string.IsNullOrWhiteSpace(app.DefaultPolicyOid)
                && visibleOids.Contains(app.DefaultPolicyOid))
            {
                defaultPolicyOid = app.DefaultPolicyOid;
            }
        }

        defaultPolicyOid ??= spifs
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.PolicyOid)
            .FirstOrDefault();

        var responseSpifs = spifs
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new VisibleSpif
            {
                PolicyOid = s.PolicyOid,
                Name = s.Name,
                SchemaVersion = s.SchemaVersion,
                ClassificationCount = s.ClassificationCount,
                CategoryCount = s.CategoryCount,
                Hash = s.Hash,
                IsActive = s.IsActive
            })
            .ToList();

        return Ok(new VisibleSpifsResponse
        {
            TenantId = tenantId,
            SubjectId = subjectId,
            DefaultPolicyOid = defaultPolicyOid,
            Spifs = responseSpifs
        });
    }

    /// <summary>Response shape for <c>GET /pdp/api/spifs/visible</c>.</summary>
    public sealed class VisibleSpifsResponse
    {
        /// <summary>Tenant the response was scoped to.</summary>
        public required string TenantId { get; init; }

        /// <summary>Subject id the response was generated for.</summary>
        public required string SubjectId { get; init; }

        /// <summary>
        /// SPIF the add-in should pre-select. Null when the user is entitled
        /// to no SPIFs at all.
        /// </summary>
        public string? DefaultPolicyOid { get; init; }

        /// <summary>SPIFs the subject is allowed to see, sorted by name.</summary>
        public required List<VisibleSpif> Spifs { get; init; }
    }

    /// <summary>One row in the visibility response.</summary>
    public sealed class VisibleSpif
    {
        /// <summary>Globally unique policy OID.</summary>
        public required string PolicyOid { get; init; }

        /// <summary>Human-readable policy name.</summary>
        public required string Name { get; init; }

        /// <summary>SPIF schema version.</summary>
        public required string SchemaVersion { get; init; }

        /// <summary>Number of classifications defined in the SPIF.</summary>
        public int ClassificationCount { get; init; }

        /// <summary>Total category count across all tag sets.</summary>
        public int CategoryCount { get; init; }

        /// <summary>Content hash for change detection on the client side.</summary>
        public required string Hash { get; init; }

        /// <summary>Whether the SPIF is currently active.</summary>
        public bool IsActive { get; init; }
    }
}
