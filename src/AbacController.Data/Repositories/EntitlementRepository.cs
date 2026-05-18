using AbacController.Core.Domain.Entitlements;
using AbacController.Core.Interfaces;
using AbacController.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IEntitlementRepository"/> with per-tenant
/// isolation. Null <c>TenantId</c> rows are treated as system-wide and are visible to
/// every tenant (matching <see cref="ApplicationRepository"/>'s pattern).
/// </summary>
public sealed class EntitlementRepository : IEntitlementRepository
{
    private readonly AbacDbContext _db;

    public EntitlementRepository(AbacDbContext db)
    {
        _db = db;
    }

    // ------------------------------------------------------------------
    // Baseline
    // ------------------------------------------------------------------

    public async Task<List<EntitlementGrant>> GetBaselineAsync(string tenantId, CancellationToken ct = default)
    {
        var rows = await _db.TenantBaselineEntitlements
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId || e.TenantId == null)
            .OrderBy(e => e.PolicyOid)
            .ThenBy(e => e.ClassificationLacv)
            .ToListAsync(ct);

        return rows.Select(r => new EntitlementGrant
        {
            TenantId = r.TenantId,
            Scope = EntitlementScope.Baseline,
            TargetId = null,
            PolicyOid = r.PolicyOid,
            ClassificationLacv = r.ClassificationLacv,
            TagSetOid = r.TagSetOid
        }).ToList();
    }

    public async Task<EntitlementGrant> AddBaselineAsync(EntitlementGrant grant, CancellationToken ct = default)
    {
        var entity = new TenantBaselineEntitlementEntity
        {
            TenantId = grant.TenantId,
            PolicyOid = grant.PolicyOid,
            ClassificationLacv = grant.ClassificationLacv,
            TagSetOid = grant.TagSetOid,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.TenantBaselineEntitlements.Add(entity);
        await _db.SaveChangesAsync(ct);
        return grant with { Scope = EntitlementScope.Baseline };
    }

    public async Task<bool> RemoveBaselineAsync(string tenantId, string policyOid, int? classificationLacv, CancellationToken ct = default)
    {
        var entity = await _db.TenantBaselineEntitlements
            .FirstOrDefaultAsync(e =>
                e.TenantId == tenantId
                && e.PolicyOid == policyOid
                && e.ClassificationLacv == classificationLacv, ct);

        if (entity is null) return false;
        _db.TenantBaselineEntitlements.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ------------------------------------------------------------------
    // Group
    // ------------------------------------------------------------------

    public async Task<List<EntitlementGrant>> GetGroupEntitlementsAsync(string tenantId, string groupId, CancellationToken ct = default)
    {
        var rows = await _db.GroupEntitlements
            .AsNoTracking()
            .Where(e => (e.TenantId == tenantId || e.TenantId == null) && e.GroupId == groupId)
            .OrderBy(e => e.PolicyOid)
            .ToListAsync(ct);

        return rows.Select(MapGroup).ToList();
    }

    public async Task<List<EntitlementGrant>> GetAllGroupEntitlementsAsync(string tenantId, IReadOnlyCollection<string> groupIds, CancellationToken ct = default)
    {
        if (groupIds.Count == 0) return [];

        var rows = await _db.GroupEntitlements
            .AsNoTracking()
            .Where(e => (e.TenantId == tenantId || e.TenantId == null) && groupIds.Contains(e.GroupId))
            .ToListAsync(ct);

        return rows.Select(MapGroup).ToList();
    }

    public async Task<EntitlementGrant> AddGroupEntitlementAsync(EntitlementGrant grant, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(grant.TargetId))
            throw new ArgumentException("Group entitlement requires a TargetId (group id).", nameof(grant));

        var entity = new GroupEntitlementEntity
        {
            TenantId = grant.TenantId,
            GroupId = grant.TargetId,
            PolicyOid = grant.PolicyOid,
            ClassificationLacv = grant.ClassificationLacv,
            TagSetOid = grant.TagSetOid,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.GroupEntitlements.Add(entity);
        await _db.SaveChangesAsync(ct);
        return grant with { Scope = EntitlementScope.Group };
    }

    public async Task<bool> RemoveGroupEntitlementAsync(string tenantId, string groupId, string policyOid, int? classificationLacv, CancellationToken ct = default)
    {
        var entity = await _db.GroupEntitlements
            .FirstOrDefaultAsync(e =>
                e.TenantId == tenantId
                && e.GroupId == groupId
                && e.PolicyOid == policyOid
                && e.ClassificationLacv == classificationLacv, ct);

        if (entity is null) return false;
        _db.GroupEntitlements.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ------------------------------------------------------------------
    // User overrides
    // ------------------------------------------------------------------

    public async Task<(List<EntitlementGrant> Grants, List<EntitlementDeny> Denies)> GetUserOverridesAsync(string tenantId, string userId, CancellationToken ct = default)
    {
        var rows = await _db.UserEntitlementOverrides
            .AsNoTracking()
            .Where(e => (e.TenantId == tenantId || e.TenantId == null) && e.UserId == userId)
            .ToListAsync(ct);

        var grants = new List<EntitlementGrant>();
        var denies = new List<EntitlementDeny>();
        foreach (var r in rows)
        {
            if (r.Mode == UserEntitlementOverrideMode.Grant)
            {
                grants.Add(new EntitlementGrant
                {
                    TenantId = r.TenantId,
                    Scope = EntitlementScope.User,
                    TargetId = r.UserId,
                    PolicyOid = r.PolicyOid,
                    ClassificationLacv = r.ClassificationLacv,
                    TagSetOid = r.TagSetOid
                });
            }
            else
            {
                denies.Add(new EntitlementDeny
                {
                    TenantId = r.TenantId,
                    UserId = r.UserId,
                    PolicyOid = r.PolicyOid,
                    ClassificationLacv = r.ClassificationLacv,
                    TagSetOid = r.TagSetOid,
                    Reason = r.Reason
                });
            }
        }
        return (grants, denies);
    }

    public async Task<EntitlementGrant> AddUserGrantAsync(EntitlementGrant grant, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(grant.TargetId))
            throw new ArgumentException("User entitlement requires a TargetId (user id).", nameof(grant));

        var entity = new UserEntitlementOverrideEntity
        {
            TenantId = grant.TenantId,
            UserId = grant.TargetId,
            PolicyOid = grant.PolicyOid,
            ClassificationLacv = grant.ClassificationLacv,
            TagSetOid = grant.TagSetOid,
            Mode = UserEntitlementOverrideMode.Grant,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.UserEntitlementOverrides.Add(entity);
        await _db.SaveChangesAsync(ct);
        return grant with { Scope = EntitlementScope.User };
    }

    public async Task<EntitlementDeny> AddUserDenyAsync(EntitlementDeny deny, CancellationToken ct = default)
    {
        var entity = new UserEntitlementOverrideEntity
        {
            TenantId = deny.TenantId,
            UserId = deny.UserId,
            PolicyOid = deny.PolicyOid,
            ClassificationLacv = deny.ClassificationLacv,
            TagSetOid = deny.TagSetOid,
            Mode = UserEntitlementOverrideMode.Deny,
            Reason = deny.Reason,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.UserEntitlementOverrides.Add(entity);
        await _db.SaveChangesAsync(ct);
        return deny;
    }

    public async Task<bool> RemoveUserOverrideAsync(string tenantId, string userId, string policyOid, int? classificationLacv, CancellationToken ct = default)
    {
        var rows = await _db.UserEntitlementOverrides
            .Where(e =>
                e.TenantId == tenantId
                && e.UserId == userId
                && e.PolicyOid == policyOid
                && e.ClassificationLacv == classificationLacv)
            .ToListAsync(ct);

        if (rows.Count == 0) return false;
        _db.UserEntitlementOverrides.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static EntitlementGrant MapGroup(GroupEntitlementEntity r) => new()
    {
        TenantId = r.TenantId,
        Scope = EntitlementScope.Group,
        TargetId = r.GroupId,
        PolicyOid = r.PolicyOid,
        ClassificationLacv = r.ClassificationLacv,
        TagSetOid = r.TagSetOid
    };
}
