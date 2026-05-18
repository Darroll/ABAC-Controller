using AbacController.Core.Domain.Entitlements;

namespace AbacController.Core.Interfaces;

/// <summary>
/// CRUD operations for tenant baseline, group and user entitlements.
/// All reads are scoped to the current tenant context via the repository's internal
/// tenant filter (matching <see cref="IApplicationRepository"/>'s pattern).
/// </summary>
public interface IEntitlementRepository
{
    /// <summary>List the tenant's baseline entitlements.</summary>
    Task<List<EntitlementGrant>> GetBaselineAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Add a baseline entitlement for a tenant.</summary>
    Task<EntitlementGrant> AddBaselineAsync(EntitlementGrant grant, CancellationToken ct = default);

    /// <summary>Remove a baseline entitlement. Returns true when a row was removed.</summary>
    Task<bool> RemoveBaselineAsync(string tenantId, string policyOid, int? classificationLacv, CancellationToken ct = default);

    /// <summary>List the entitlements for a directory group in a tenant.</summary>
    Task<List<EntitlementGrant>> GetGroupEntitlementsAsync(string tenantId, string groupId, CancellationToken ct = default);

    /// <summary>List ALL group entitlements for a tenant — used by the resolver to compute effective sets.</summary>
    Task<List<EntitlementGrant>> GetAllGroupEntitlementsAsync(string tenantId, IReadOnlyCollection<string> groupIds, CancellationToken ct = default);

    /// <summary>Add a group entitlement.</summary>
    Task<EntitlementGrant> AddGroupEntitlementAsync(EntitlementGrant grant, CancellationToken ct = default);

    /// <summary>Remove a group entitlement.</summary>
    Task<bool> RemoveGroupEntitlementAsync(string tenantId, string groupId, string policyOid, int? classificationLacv, CancellationToken ct = default);

    /// <summary>List grants and denies for a specific user.</summary>
    Task<(List<EntitlementGrant> Grants, List<EntitlementDeny> Denies)> GetUserOverridesAsync(string tenantId, string userId, CancellationToken ct = default);

    /// <summary>Add a user grant.</summary>
    Task<EntitlementGrant> AddUserGrantAsync(EntitlementGrant grant, CancellationToken ct = default);

    /// <summary>Add a user deny.</summary>
    Task<EntitlementDeny> AddUserDenyAsync(EntitlementDeny deny, CancellationToken ct = default);

    /// <summary>Remove a user override (grant or deny) by composite key.</summary>
    Task<bool> RemoveUserOverrideAsync(string tenantId, string userId, string policyOid, int? classificationLacv, CancellationToken ct = default);
}
