using AbacController.Core.Domain.Groups;
using AbacController.Core.Interfaces;
using AbacController.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAbacGroupRepository"/>. Tenant isolation
/// follows the same convention as <see cref="ApplicationRepository"/> — null
/// <c>TenantId</c> rows are treated as system-wide and visible to every tenant.
/// </summary>
public sealed class AbacGroupRepository : IAbacGroupRepository
{
    private readonly AbacDbContext _db;

    public AbacGroupRepository(AbacDbContext db)
    {
        _db = db;
    }

    // ── Group CRUD ──────────────────────────────────────────────────────────

    public async Task<List<AbacGroup>> ListGroupsAsync(string tenantId, CancellationToken ct = default)
    {
        // SQLite cannot ORDER BY DateTimeOffset; do the ordering client-side
        // exactly like AuditRepository / WebhookRepository do.
        var rows = await _db.AbacGroups
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId || e.TenantId == null)
            .ToListAsync(ct);

        return rows
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MapGroup)
            .ToList();
    }

    public async Task<AbacGroup?> GetGroupAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.AbacGroups.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return row is null ? null : MapGroup(row);
    }

    public async Task<AbacGroup> CreateGroupAsync(AbacGroup group, CancellationToken ct = default)
    {
        var entity = new AbacGroupEntity
        {
            Id = group.Id == Guid.Empty ? Guid.NewGuid() : group.Id,
            TenantId = group.TenantId,
            Name = group.Name,
            Description = group.Description,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.AbacGroups.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapGroup(entity);
    }

    public async Task<bool> DeleteGroupAsync(Guid id, CancellationToken ct = default)
    {
        var group = await _db.AbacGroups.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (group is null) return false;

        var memberships = await _db.AbacGroupMemberships
            .Where(e => e.AbacGroupId == id)
            .ToListAsync(ct);
        _db.AbacGroupMemberships.RemoveRange(memberships);
        _db.AbacGroups.Remove(group);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ── Membership CRUD ─────────────────────────────────────────────────────

    public async Task<List<AbacGroupMembership>> ListMembersAsync(Guid groupId, CancellationToken ct = default)
    {
        var rows = await _db.AbacGroupMemberships
            .AsNoTracking()
            .Where(e => e.AbacGroupId == groupId)
            .ToListAsync(ct);

        return rows
            .OrderBy(e => e.Kind)
            .ThenBy(e => e.MemberId, StringComparer.OrdinalIgnoreCase)
            .Select(MapMembership)
            .ToList();
    }

    public async Task<AbacGroupMembership> AddMemberAsync(AbacGroupMembership membership, CancellationToken ct = default)
    {
        var existing = await _db.AbacGroupMemberships.FirstOrDefaultAsync(
            e => e.AbacGroupId == membership.AbacGroupId
                 && e.Kind == (int)membership.Kind
                 && e.MemberId == membership.MemberId,
            ct);

        if (existing is not null)
        {
            return MapMembership(existing);
        }

        var entity = new AbacGroupMembershipEntity
        {
            AbacGroupId = membership.AbacGroupId,
            Kind = (int)membership.Kind,
            MemberId = membership.MemberId,
            TenantId = membership.TenantId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.AbacGroupMemberships.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapMembership(entity);
    }

    public async Task<bool> RemoveMemberAsync(Guid groupId, AbacGroupMemberKind kind, string memberId, CancellationToken ct = default)
    {
        var entity = await _db.AbacGroupMemberships.FirstOrDefaultAsync(
            e => e.AbacGroupId == groupId
                 && e.Kind == (int)kind
                 && e.MemberId == memberId,
            ct);

        if (entity is null) return false;
        _db.AbacGroupMemberships.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ── Hot path: resolver ──────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<Guid>> ResolveAbacGroupIdsAsync(
        string tenantId,
        string userId,
        IReadOnlyCollection<string> keycloakGroupIds,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) && keycloakGroupIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var userKind = (int)AbacGroupMemberKind.User;
        var kgKind = (int)AbacGroupMemberKind.KeycloakGroup;

        // One round trip: WHERE TenantId matches AND ((Kind=User AND MemberId=uid)
        // OR (Kind=KeycloakGroup AND MemberId IN @kgs)). The composite index on
        // (TenantId, Kind, MemberId) makes both branches index seeks.
        var query = _db.AbacGroupMemberships
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId || e.TenantId == null);

        if (keycloakGroupIds.Count > 0 && !string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(e =>
                (e.Kind == userKind && e.MemberId == userId)
                || (e.Kind == kgKind && keycloakGroupIds.Contains(e.MemberId)));
        }
        else if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(e => e.Kind == userKind && e.MemberId == userId);
        }
        else
        {
            query = query.Where(e => e.Kind == kgKind && keycloakGroupIds.Contains(e.MemberId));
        }

        var ids = await query.Select(e => e.AbacGroupId).Distinct().ToListAsync(ct);
        return ids;
    }

    // ── Mapping helpers ─────────────────────────────────────────────────────

    private static AbacGroup MapGroup(AbacGroupEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        Name = e.Name,
        Description = e.Description,
        CreatedAt = e.CreatedAt
    };

    private static AbacGroupMembership MapMembership(AbacGroupMembershipEntity e) => new()
    {
        AbacGroupId = e.AbacGroupId,
        Kind = (AbacGroupMemberKind)e.Kind,
        MemberId = e.MemberId,
        TenantId = e.TenantId,
        CreatedAt = e.CreatedAt
    };
}
