using AbacController.Core.Domain.Attributes;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Pluggable attribute source interface. Implementations fetch attributes
/// from external systems (LDAP, OIDC, REST, SQL, etc.).
/// </summary>
public interface IPipSource
{
    /// <summary>Source type identifier (e.g., "ldap", "oidc", "rest", "database", "static").</summary>
    string SourceType { get; }

    /// <summary>Configured source instance ID.</summary>
    string SourceId { get; }

    /// <summary>Attribute names this source provides.</summary>
    IReadOnlySet<string> ProvidesAttributes { get; }

    /// <summary>Priority (lower = higher priority).</summary>
    int Priority { get; }

    /// <summary>Resolve attributes for a given subject.</summary>
    Task<AttributeResolutionResult> ResolveAsync(
        AttributeResolutionRequest request,
        CancellationToken ct = default);

    /// <summary>Test connectivity to the attribute source.</summary>
    Task<SourceHealthResult> TestConnectivityAsync(CancellationToken ct = default);
}

/// <summary>
/// Attribute resolution orchestrator. Manages multi-layer resolution.
/// </summary>
public interface IPipResolver
{
    /// <summary>Resolve attributes using the 3-layer strategy.</summary>
    Task<AttributeResolutionResult> ResolveAsync(
        AttributeResolutionRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Attribute cache management.
/// </summary>
public interface IPipCacheManager
{
    /// <summary>Try to get a cached attribute value.</summary>
    bool TryGet(string sourceId, string subjectId, string attributeName, out AttributeValue? value);

    /// <summary>Cache an attribute value.</summary>
    void Set(string sourceId, string subjectId, AttributeValue value, TimeSpan ttl);

    /// <summary>Invalidate all cached values for a subject.</summary>
    void Invalidate(string subjectId);

    /// <summary>Invalidate all cached values.</summary>
    void InvalidateAll();
}
