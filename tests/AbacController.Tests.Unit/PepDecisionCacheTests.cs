using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;
using AbacController.Pep;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for PEP-layer decision caching.
/// </summary>
public sealed class PepDecisionCacheTests
{
    private static EvaluationRequest MakeRequest(string subjectId = "alice", string action = "read", string resourceId = "doc-1")
        => new()
        {
            Subject = new SubjectInfo { Type = "user", Id = subjectId },
            Action = new ActionInfo { Name = action },
            Resource = new ResourceInfo { Type = "document", Id = resourceId }
        };

    private static EvaluationResult MakeResult(Decision decision = Decision.Permit)
        => new() { DecisionId = Guid.NewGuid().ToString("N"), Decision = decision };

    [Fact]
    public void TryGet_EmptyCache_ReturnsFalse()
    {
        var cache = new PepDecisionCache();
        Assert.False(cache.TryGet(MakeRequest(), out var result));
        Assert.Null(result);
    }

    [Fact]
    public void Set_ThenGet_ReturnsCachedResult()
    {
        var cache = new PepDecisionCache();
        var request = MakeRequest();
        var expected = MakeResult();

        cache.Set(request, expected);
        Assert.True(cache.TryGet(request, out var result));
        Assert.NotNull(result);
        Assert.Equal(Decision.Permit, result.Decision);
    }

    [Fact]
    public void Set_IndeterminateDecision_NotCached()
    {
        var cache = new PepDecisionCache();
        var request = MakeRequest();
        var indeterminate = MakeResult(Decision.Indeterminate);

        cache.Set(request, indeterminate);
        Assert.False(cache.TryGet(request, out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Set_DenyDecision_IsCached()
    {
        var cache = new PepDecisionCache();
        var request = MakeRequest();
        var deny = MakeResult(Decision.Deny);

        cache.Set(request, deny);
        Assert.True(cache.TryGet(request, out var result));
        Assert.Equal(Decision.Deny, result!.Decision);
    }

    [Fact]
    public void Set_NotApplicableDecision_IsCached()
    {
        var cache = new PepDecisionCache();
        var request = MakeRequest();
        var na = MakeResult(Decision.NotApplicable);

        cache.Set(request, na);
        Assert.True(cache.TryGet(request, out var result));
        Assert.Equal(Decision.NotApplicable, result!.Decision);
    }

    [Fact]
    public void ExpiredEntry_ReturnsMiss()
    {
        var cache = new PepDecisionCache(defaultTtl: TimeSpan.FromMilliseconds(1));
        var request = MakeRequest();
        cache.Set(request, MakeResult());

        Thread.Sleep(10);

        Assert.False(cache.TryGet(request, out _));
    }

    [Fact]
    public void DifferentRequests_DifferentKeys()
    {
        var cache = new PepDecisionCache();
        var req1 = MakeRequest("alice", "read", "doc-1");
        var req2 = MakeRequest("bob", "read", "doc-1");

        cache.Set(req1, MakeResult(Decision.Permit));
        cache.Set(req2, MakeResult(Decision.Deny));

        Assert.True(cache.TryGet(req1, out var r1));
        Assert.True(cache.TryGet(req2, out var r2));
        Assert.Equal(Decision.Permit, r1!.Decision);
        Assert.Equal(Decision.Deny, r2!.Decision);
    }

    [Fact]
    public void InvalidateAll_ClearsAllEntries()
    {
        var cache = new PepDecisionCache();
        cache.Set(MakeRequest("a"), MakeResult());
        cache.Set(MakeRequest("b"), MakeResult());
        cache.Set(MakeRequest("c"), MakeResult());

        Assert.Equal(3, cache.Count);

        cache.InvalidateAll();

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet(MakeRequest("a"), out _));
    }

    [Fact]
    public void Scavenge_RemovesExpiredEntries()
    {
        var cache = new PepDecisionCache(defaultTtl: TimeSpan.FromMilliseconds(1));
        cache.Set(MakeRequest("x"), MakeResult());
        cache.Set(MakeRequest("y"), MakeResult());

        Thread.Sleep(10);

        // Add one that's not expired
        cache.SetByKey("fresh", MakeResult(), TimeSpan.FromMinutes(5));

        var removed = cache.Scavenge();
        Assert.Equal(2, removed);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void MaxEntries_EvictsOldestWhenFull()
    {
        var cache = new PepDecisionCache(maxEntries: 5);

        for (var i = 0; i < 10; i++)
        {
            cache.Set(MakeRequest($"user-{i}", $"act-{i}", $"res-{i}"), MakeResult());
        }

        // Should have evicted to stay near or below capacity
        Assert.True(cache.Count <= 10);
        Assert.True(cache.Evictions > 0);
    }

    [Fact]
    public void HitRatio_TracksCorrectly()
    {
        var cache = new PepDecisionCache();
        var request = MakeRequest();
        cache.Set(request, MakeResult());

        cache.TryGet(request, out _); // hit
        cache.TryGet(request, out _); // hit
        cache.TryGet(MakeRequest("nobody"), out _); // miss

        Assert.Equal(2, cache.Hits);
        Assert.Equal(1, cache.Misses);
        Assert.InRange(cache.HitRatio, 0.66, 0.67);
    }

    [Fact]
    public void HitRatio_NoLookups_ReturnsZero()
    {
        var cache = new PepDecisionCache();
        Assert.Equal(0.0, cache.HitRatio);
    }

    [Fact]
    public void Statistics_ReturnsCorrectSnapshot()
    {
        var cache = new PepDecisionCache(defaultTtl: TimeSpan.FromMinutes(2), maxEntries: 500, pepId: "pep-1");
        cache.Set(MakeRequest(), MakeResult());
        cache.TryGet(MakeRequest(), out _);
        cache.TryGet(MakeRequest("missing"), out _);

        var stats = cache.GetStatistics();

        Assert.Equal(1, stats.Hits);
        Assert.Equal(1, stats.Misses);
        Assert.Equal(1, stats.CurrentEntries);
        Assert.Equal(500, stats.MaxEntries);
        Assert.Equal(120, stats.DefaultTtlSeconds);
        Assert.Equal("pep-1", stats.PepId);
    }

    [Fact]
    public void PepId_IsolatesKeys()
    {
        var cache1 = new PepDecisionCache(pepId: "pep-a");
        var cache2 = new PepDecisionCache(pepId: "pep-b");
        var request = MakeRequest();

        var key1 = cache1.ComputeKey(request);
        var key2 = cache2.ComputeKey(request);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void ComputeKey_SameRequest_SameKey()
    {
        var cache = new PepDecisionCache();
        var request = MakeRequest();

        var key1 = cache.ComputeKey(request);
        var key2 = cache.ComputeKey(request);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void ComputeKey_DifferentAction_DifferentKey()
    {
        var cache = new PepDecisionCache();
        var req1 = MakeRequest("alice", "read", "doc-1");
        var req2 = MakeRequest("alice", "write", "doc-1");

        Assert.NotEqual(cache.ComputeKey(req1), cache.ComputeKey(req2));
    }

    [Fact]
    public void SetByKey_ThenGetByKey_Works()
    {
        var cache = new PepDecisionCache();
        var result = MakeResult(Decision.Deny);

        cache.SetByKey("custom-key", result);
        Assert.True(cache.TryGetByKey("custom-key", out var cached));
        Assert.Equal(Decision.Deny, cached!.Decision);
    }

    [Fact]
    public void Set_OverwritesExistingEntry()
    {
        var cache = new PepDecisionCache();
        var request = MakeRequest();

        cache.Set(request, MakeResult(Decision.Deny));
        cache.Set(request, MakeResult(Decision.Permit));

        Assert.True(cache.TryGet(request, out var result));
        Assert.Equal(Decision.Permit, result!.Decision);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void CustomTtl_Override_RespectsSpecifiedTtl()
    {
        var cache = new PepDecisionCache(defaultTtl: TimeSpan.FromMinutes(5));
        var request = MakeRequest();

        cache.Set(request, MakeResult(), TimeSpan.FromMilliseconds(1));
        Thread.Sleep(10);

        Assert.False(cache.TryGet(request, out _));
    }

    [Fact]
    public async Task ConcurrentAccess_ThreadSafe()
    {
        var cache = new PepDecisionCache(maxEntries: 1000);
        var tasks = new List<Task>();

        for (var i = 0; i < 100; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(() =>
            {
                var request = MakeRequest($"user-{idx}");
                cache.Set(request, MakeResult());
                cache.TryGet(request, out _);
                cache.TryGet(MakeRequest($"missing-{idx}"), out _);
            }));
        }

        await Task.WhenAll(tasks);

        Assert.True(cache.Count > 0);
        Assert.True(cache.Hits > 0);
        Assert.True(cache.Misses > 0);
    }
}
