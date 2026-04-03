using Microsoft.Extensions.Caching.Memory;
using AbacController.Core.Domain.Attributes;
using AbacController.Core.Interfaces;

namespace AbacController.Pip;

/// <summary>
/// Per-source attribute cache backed by MemoryCache.
/// Thread-safe, shared across requests.
/// </summary>
public sealed class PipCacheManager : IPipCacheManager
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions
    {
        SizeLimit = 50_000
    });

    /// <inheritdoc />
    public bool TryGet(string sourceId, string subjectId, string attributeName, out AttributeValue? value)
    {
        var key = MakeKey(sourceId, subjectId, attributeName);
        return _cache.TryGetValue(key, out value);
    }

    /// <inheritdoc />
    public void Set(string sourceId, string subjectId, AttributeValue value, TimeSpan ttl)
    {
        var key = MakeKey(sourceId, subjectId, value.Name);
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
            Size = 1
        };
        _cache.Set(key, value, options);
    }

    /// <inheritdoc />
    public void Invalidate(string subjectId)
    {
        // MemoryCache doesn't support prefix-based invalidation.
        // For v1, compact the entire cache.
        _cache.Compact(1.0);
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        _cache.Compact(1.0);
    }

    private static string MakeKey(string sourceId, string subjectId, string attributeName)
        => $"{sourceId}:{subjectId}:{attributeName}";
}
