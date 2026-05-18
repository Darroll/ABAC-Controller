namespace AbacController.Core.Interfaces;

/// <summary>
/// Resolves the effective set of ABAC group ids for a subject. Layers caching on
/// top of <see cref="IAbacGroupRepository.ResolveAbacGroupIdsAsync"/> so the same
/// (tenant, user, keycloak-groups) tuple is fetched at most once per cache TTL.
/// </summary>
public interface IGroupMembershipResolver
{
    /// <summary>
    /// Compute the effective ABAC group ids the subject belongs to. The result
    /// is the union of (a) ABAC groups where the subject is a direct user member
    /// and (b) ABAC groups that reference any of the subject's Keycloak groups.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> ResolveAsync(
        string tenantId,
        string userId,
        IReadOnlyCollection<string> keycloakGroupIds,
        CancellationToken ct = default);

    /// <summary>
    /// Drop any cached entries for the given tenant. Called from the group/membership
    /// admin write paths so a freshly added member doesn't have to wait for the TTL.
    /// </summary>
    void InvalidateTenant(string tenantId);
}

/// <summary>
/// In-memory cache for the membership resolver. Keyed on
/// <c>(tenantId, userId, hash(sortedKeycloakGroupIds))</c> with a short TTL so
/// short-lived web requests bypass the database.
/// </summary>
public interface IGroupMembershipCache
{
    /// <summary>Try to fetch a cached entry. Returns true on hit.</summary>
    bool TryGet(string tenantId, string userId, IReadOnlyCollection<string> keycloakGroupIds, out IReadOnlyCollection<Guid> abacGroupIds);

    /// <summary>Store a freshly resolved entry under the same key shape.</summary>
    void Set(string tenantId, string userId, IReadOnlyCollection<string> keycloakGroupIds, IReadOnlyCollection<Guid> abacGroupIds);

    /// <summary>Drop every cached entry for the given tenant.</summary>
    void InvalidateTenant(string tenantId);
}
