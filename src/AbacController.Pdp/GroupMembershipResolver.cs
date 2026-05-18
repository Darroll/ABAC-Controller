using AbacController.Core.Interfaces;

namespace AbacController.Pdp;

/// <summary>
/// Default <see cref="IGroupMembershipResolver"/>. Looks up the cache first; on
/// miss it calls the repository and stores the result. Tenant invalidation is a
/// straight pass-through to the cache.
/// </summary>
public sealed class GroupMembershipResolver : IGroupMembershipResolver
{
    private readonly IAbacGroupRepository _repository;
    private readonly IGroupMembershipCache _cache;

    public GroupMembershipResolver(IAbacGroupRepository repository, IGroupMembershipCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<IReadOnlyCollection<Guid>> ResolveAsync(
        string tenantId,
        string userId,
        IReadOnlyCollection<string> keycloakGroupIds,
        CancellationToken ct = default)
    {
        if (_cache.TryGet(tenantId, userId, keycloakGroupIds, out var cached))
        {
            return cached;
        }

        var resolved = await _repository.ResolveAbacGroupIdsAsync(tenantId, userId, keycloakGroupIds, ct);
        _cache.Set(tenantId, userId, keycloakGroupIds, resolved);
        return resolved;
    }

    public void InvalidateTenant(string tenantId) => _cache.InvalidateTenant(tenantId);
}
