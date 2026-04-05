using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;

namespace AbacController.Pep;

/// <summary>
/// Lightweight, short-lived in-memory cache at the PEP layer to reduce PDP calls.
/// Designed for high-throughput enforcement points where the same subject/action/resource
/// combination is checked repeatedly within a short window (e.g., streaming, batch operations).
///
/// Unlike the PDP-layer <see cref="Pdp.DecisionCache"/>, this cache:
/// - Lives at the PEP boundary, closer to the caller.
/// - Uses shorter TTLs (default 30 seconds) for rapid policy change responsiveness.
/// - Has a bounded capacity with LRU-style eviction to prevent memory pressure.
/// - Supports per-enforcement-point isolation via optional prefix keys.
/// - Is thread-safe for concurrent PEP access.
/// </summary>
public sealed class PepDecisionCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly TimeSpan _defaultTtl;
    private readonly int _maxEntries;
    private readonly string? _pepId;
    private long _hits;
    private long _misses;
    private long _evictions;

    /// <summary>
    /// Creates a new PEP decision cache.
    /// </summary>
    /// <param name="defaultTtl">Default TTL for cached decisions. Defaults to 30 seconds.</param>
    /// <param name="maxEntries">Maximum number of cached entries before eviction. Defaults to 10,000.</param>
    /// <param name="pepId">Optional PEP identifier for key isolation.</param>
    public PepDecisionCache(TimeSpan? defaultTtl = null, int maxEntries = 10_000, string? pepId = null)
    {
        _defaultTtl = defaultTtl ?? TimeSpan.FromSeconds(30);
        _maxEntries = maxEntries > 0 ? maxEntries : 10_000;
        _pepId = pepId;
    }

    /// <summary>Total cache hits since creation.</summary>
    public long Hits => Interlocked.Read(ref _hits);

    /// <summary>Total cache misses since creation.</summary>
    public long Misses => Interlocked.Read(ref _misses);

    /// <summary>Total evictions since creation.</summary>
    public long Evictions => Interlocked.Read(ref _evictions);

    /// <summary>Current number of entries in the cache.</summary>
    public int Count => _cache.Count;

    /// <summary>Cache hit ratio (0.0 to 1.0). Returns 0 if no lookups have been made.</summary>
    public double HitRatio
    {
        get
        {
            var total = Hits + Misses;
            return total == 0 ? 0.0 : (double)Hits / total;
        }
    }

    /// <summary>
    /// Try to get a cached decision for the given request.
    /// Expired entries are treated as misses and removed.
    /// </summary>
    /// <param name="request">The evaluation request to look up.</param>
    /// <param name="result">The cached result, if found and not expired.</param>
    /// <returns>True if a valid cached result was found.</returns>
    public bool TryGet(EvaluationRequest request, out EvaluationResult? result)
    {
        var key = ComputeKey(request);
        return TryGetByKey(key, out result);
    }

    /// <summary>
    /// Try to get a cached decision by pre-computed cache key.
    /// </summary>
    public bool TryGetByKey(string key, out EvaluationResult? result)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
            {
                entry.LastAccessedAt = DateTimeOffset.UtcNow;
                result = entry.Result;
                Interlocked.Increment(ref _hits);
                return true;
            }

            // Expired — remove
            _cache.TryRemove(key, out _);
        }

        result = null;
        Interlocked.Increment(ref _misses);
        return false;
    }

    /// <summary>
    /// Cache a decision result with the default TTL.
    /// </summary>
    public void Set(EvaluationRequest request, EvaluationResult result)
        => Set(request, result, _defaultTtl);

    /// <summary>
    /// Cache a decision result with a specific TTL.
    /// </summary>
    public void Set(EvaluationRequest request, EvaluationResult result, TimeSpan ttl)
    {
        var key = ComputeKey(request);
        SetByKey(key, result, ttl);
    }

    /// <summary>
    /// Cache a decision result by pre-computed cache key.
    /// </summary>
    public void SetByKey(string key, EvaluationResult result, TimeSpan? ttl = null)
    {
        // Don't cache Indeterminate decisions — they represent errors
        if (result.Decision == Decision.Indeterminate)
            return;

        EvictIfNeeded();

        var effectiveTtl = ttl ?? _defaultTtl;
        var entry = new CacheEntry
        {
            Result = result,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(effectiveTtl),
            LastAccessedAt = DateTimeOffset.UtcNow
        };

        _cache[key] = entry;
    }

    /// <summary>
    /// Compute a cache key for the given request.
    /// The key incorporates subject, action, resource, and optional policy set ID.
    /// </summary>
    public string ComputeKey(EvaluationRequest request)
    {
        var sb = new StringBuilder(256);

        if (_pepId is not null)
            sb.Append(_pepId).Append(':');

        sb.Append(request.Subject.Id)
          .Append('|')
          .Append(request.Subject.Type)
          .Append('|')
          .Append(request.Action.Name)
          .Append('|')
          .Append(request.Resource.Type)
          .Append('|')
          .Append(request.Resource.Id)
          .Append('|')
          .Append(request.Options.PolicySetId ?? "-");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Invalidate all entries for a specific subject.
    /// Useful when a user's clearance changes.
    /// </summary>
    public int InvalidateBySubject(string subjectId)
    {
        var keysToRemove = _cache
            .Where(kvp => kvp.Value.SubjectId == subjectId)
            .Select(kvp => kvp.Key)
            .ToList();

        var removed = 0;
        foreach (var key in keysToRemove)
        {
            if (_cache.TryRemove(key, out _))
                removed++;
        }

        return removed;
    }

    /// <summary>
    /// Invalidate all cached decisions.
    /// </summary>
    public void InvalidateAll()
    {
        var count = _cache.Count;
        _cache.Clear();
        Interlocked.Add(ref _evictions, count);
    }

    /// <summary>
    /// Remove expired entries. Called automatically during Set, but can be called manually.
    /// </summary>
    public int Scavenge()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _cache
            .Where(kvp => kvp.Value.ExpiresAt <= now)
            .Select(kvp => kvp.Key)
            .ToList();

        var removed = 0;
        foreach (var key in expired)
        {
            if (_cache.TryRemove(key, out _))
                removed++;
        }

        return removed;
    }

    /// <summary>
    /// Get cache statistics snapshot.
    /// </summary>
    public PepCacheStatistics GetStatistics()
    {
        return new PepCacheStatistics
        {
            Hits = Hits,
            Misses = Misses,
            Evictions = Evictions,
            CurrentEntries = Count,
            MaxEntries = _maxEntries,
            HitRatio = HitRatio,
            DefaultTtlSeconds = _defaultTtl.TotalSeconds,
            PepId = _pepId
        };
    }

    private void EvictIfNeeded()
    {
        if (_cache.Count < _maxEntries)
            return;

        // First pass: remove expired entries
        var removed = Scavenge();

        // If still over capacity, remove oldest-accessed entries
        if (_cache.Count >= _maxEntries)
        {
            var toRemove = _cache
                .OrderBy(kvp => kvp.Value.LastAccessedAt)
                .Take(_cache.Count - _maxEntries + (_maxEntries / 10)) // Remove 10% extra to avoid thrashing
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                if (_cache.TryRemove(key, out _))
                    removed++;
            }
        }

        Interlocked.Add(ref _evictions, removed);
    }

    private sealed class CacheEntry
    {
        /// <summary>Gets or sets the result.</summary>
        public required EvaluationResult Result { get; init; }
        /// <summary>Gets or sets the created At.</summary>
        public required DateTimeOffset CreatedAt { get; init; }
        /// <summary>Gets or sets the expires At.</summary>
        public required DateTimeOffset ExpiresAt { get; init; }
        /// <summary>Gets or sets the last Accessed At.</summary>
        public DateTimeOffset LastAccessedAt { get; set; }
        public string? SubjectId => Result.RequestId; // Use for subject-based invalidation
    }
}

/// <summary>
/// PEP cache statistics snapshot.
/// </summary>
public sealed record PepCacheStatistics
{
    /// <summary>Total cache hits.</summary>
    public long Hits { get; init; }

    /// <summary>Total cache misses.</summary>
    public long Misses { get; init; }

    /// <summary>Total evictions.</summary>
    public long Evictions { get; init; }

    /// <summary>Current number of entries.</summary>
    public int CurrentEntries { get; init; }

    /// <summary>Maximum configured entries.</summary>
    public int MaxEntries { get; init; }

    /// <summary>Hit ratio (0.0 to 1.0).</summary>
    public double HitRatio { get; init; }

    /// <summary>Default TTL in seconds.</summary>
    public double DefaultTtlSeconds { get; init; }

    /// <summary>PEP identifier, if set.</summary>
    public string? PepId { get; init; }
}
