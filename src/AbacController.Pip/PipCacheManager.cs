using System.Collections.Concurrent;
using AbacController.Core.Domain.Attributes;
using AbacController.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

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

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _subjectKeys = new(StringComparer.Ordinal);

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

        var subjectIndex = _subjectKeys.GetOrAdd(subjectId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        subjectIndex[key] = 0;

        options.RegisterPostEvictionCallback(static (evictedKey, _, _, state) =>
        {
            if (evictedKey is not string cacheKey || state is not SubjectEntryState entryState)
            {
                return;
            }

            if (entryState.SubjectKeys.TryGetValue(entryState.SubjectId, out var indexedKeys))
            {
                indexedKeys.TryRemove(cacheKey, out _);
                if (indexedKeys.IsEmpty)
                {
                    entryState.SubjectKeys.TryRemove(entryState.SubjectId, out _);
                }
            }
        }, new SubjectEntryState(subjectId, _subjectKeys));

        _cache.Set(key, value, options);
    }

    /// <inheritdoc />
    public void Invalidate(string subjectId)
    {
        if (!_subjectKeys.TryRemove(subjectId, out var keys))
        {
            return;
        }

        foreach (var key in keys.Keys)
        {
            _cache.Remove(key);
        }
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        foreach (var subjectId in _subjectKeys.Keys.ToList())
        {
            Invalidate(subjectId);
        }
    }

    private static string MakeKey(string sourceId, string subjectId, string attributeName)
        => $"{sourceId}:{subjectId}:{attributeName}";

    private sealed record SubjectEntryState(
        string SubjectId,
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> SubjectKeys);
}
