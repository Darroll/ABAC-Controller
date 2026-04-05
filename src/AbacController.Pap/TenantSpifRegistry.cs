using System.Collections.Concurrent;
using System.Collections.Immutable;
using AbacController.Core.Interfaces;

namespace AbacController.Pap;

/// <summary>
/// Shared multi-tenant SPIF registry store.
/// Maintains one isolated in-memory <see cref="SpifRegistry"/> per tenant plus a default system registry.
/// </summary>
public sealed class TenantSpifRegistryStore
{
    private readonly ConcurrentDictionary<string, SpifRegistry> _tenantRegistries = new(StringComparer.Ordinal);
    private readonly SpifRegistry _defaultRegistry = new();

    /// <summary>
    /// Gets the registry for the specified tenant.
    /// A null tenant identifier resolves to the default system registry.
    /// </summary>
    public SpifRegistry GetRegistryForTenant(string? tenantId)
    {
        if (tenantId is null)
        {
            return _defaultRegistry;
        }

        return _tenantRegistries.GetOrAdd(tenantId, static _ => new SpifRegistry());
    }

    /// <summary>
    /// Gets the known non-default tenant identifiers.
    /// </summary>
    public IReadOnlyList<string> GetKnownTenantIds()
        => _tenantRegistries.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToImmutableArray();
}

/// <summary>
/// Request-scoped tenant-aware <see cref="ISpifRegistry"/> facade. It delegates all
/// operations to the registry associated with the current tenant.
/// </summary>
public sealed class TenantSpifRegistry : ISpifRegistry
{
    private readonly TenantSpifRegistryStore _store;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantSpifRegistry"/> class.
    /// </summary>
    public TenantSpifRegistry(TenantSpifRegistryStore store, ITenantContext tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    private SpifRegistry CurrentRegistry => _store.GetRegistryForTenant(_tenantContext.TenantId);

    /// <inheritdoc />
    public ISpifIndex? GetByPolicyOid(string policyOid)
        => CurrentRegistry.GetByPolicyOid(policyOid);

    /// <inheritdoc />
    public ISpifIndex? GetDefault()
        => CurrentRegistry.GetDefault();

    /// <inheritdoc />
    public void Register(ISpifIndex spifIndex)
        => CurrentRegistry.Register(spifIndex);

    /// <inheritdoc />
    public void SetDefault(string policyOid)
        => CurrentRegistry.SetDefault(policyOid);

    /// <inheritdoc />
    public void Remove(string policyOid)
        => CurrentRegistry.Remove(policyOid);

    /// <inheritdoc />
    public IReadOnlyList<string> GetRegisteredPolicyOids()
        => CurrentRegistry.GetRegisteredPolicyOids();

    /// <inheritdoc />
    public bool IsRegistered(string policyOid)
        => CurrentRegistry.IsRegistered(policyOid);
}
