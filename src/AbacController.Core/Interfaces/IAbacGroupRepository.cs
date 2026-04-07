using AbacController.Core.Domain.Groups;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Persistence operations for ABAC groups and their memberships, plus the
/// hot-path resolver method that powers the membership resolver and the
/// classification query engine's group filter layer.
/// </summary>
public interface IAbacGroupRepository
{
    // ── Group CRUD ──────────────────────────────────────────────────────────

    /// <summary>List groups visible to the given tenant (own + system-wide).</summary>
    Task<List<AbacGroup>> ListGroupsAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Get a single group by id (any tenant).</summary>
    Task<AbacGroup?> GetGroupAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Create a new ABAC group. Throws if a group with the same name already
    /// exists for the tenant (the unique <c>(TenantId, Name)</c> index enforces it).
    /// </summary>
    Task<AbacGroup> CreateGroupAsync(AbacGroup group, CancellationToken ct = default);

    /// <summary>Delete a group and every membership row attached to it.</summary>
    Task<bool> DeleteGroupAsync(Guid id, CancellationToken ct = default);

    // ── Membership CRUD ─────────────────────────────────────────────────────

    /// <summary>List the members of a group.</summary>
    Task<List<AbacGroupMembership>> ListMembersAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>
    /// Add a membership row. Idempotent: a duplicate (Group, Kind, MemberId)
    /// returns the existing row instead of throwing.
    /// </summary>
    Task<AbacGroupMembership> AddMemberAsync(AbacGroupMembership membership, CancellationToken ct = default);

    /// <summary>Remove a membership row by composite key. Returns true when a row was deleted.</summary>
    Task<bool> RemoveMemberAsync(Guid groupId, AbacGroupMemberKind kind, string memberId, CancellationToken ct = default);

    // ── Hot path: resolver ──────────────────────────────────────────────────

    /// <summary>
    /// Compute the set of ABAC group ids the subject effectively belongs to —
    /// the union of (a) groups where the subject is a direct user member and
    /// (b) groups that reference any of the subject's Keycloak groups.
    /// </summary>
    /// <remarks>
    /// One SQL round trip backed by a covering index on
    /// <c>(TenantId, Kind, MemberId) INCLUDE AbacGroupId</c>. This fires on every
    /// classification/visibility query, so the implementation should be lean.
    /// </remarks>
    Task<IReadOnlyCollection<Guid>> ResolveAbacGroupIdsAsync(
        string tenantId,
        string userId,
        IReadOnlyCollection<string> keycloakGroupIds,
        CancellationToken ct = default);
}
