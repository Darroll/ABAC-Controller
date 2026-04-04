using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;
using AbacController.Pdp;
using Microsoft.Extensions.Caching.Memory;

namespace AbacController.Tests.Unit;

/// <summary>
/// Edge case tests for DecisionCache — key computation, invalidation, TTL.
/// </summary>
public sealed class DecisionCacheEdgeCaseTests : IDisposable
{
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly DecisionCache _cache;

    public DecisionCacheEdgeCaseTests()
    {
        _cache = new DecisionCache(_memoryCache);
    }

    public void Dispose() => _memoryCache.Dispose();

    private static EvaluationRequest MakeRequest(string subjectId = "alice", string action = "read",
        string resourceType = "doc", string resourceId = "1") => new()
    {
        Subject = new SubjectInfo { Type = "user", Id = subjectId },
        Action = new ActionInfo { Name = action },
        Resource = new ResourceInfo { Type = resourceType, Id = resourceId }
    };

    private static EvaluationResult MakeResult(Decision decision = Decision.Permit) => new()
    {
        DecisionId = Guid.NewGuid().ToString(),
        Decision = decision,
        AppliedPolicies = ["policy-set:default"]
    };

    [Fact]
    public void ComputeKey_DifferentInputs_DifferentKeys()
    {
        var key1 = _cache.ComputeKey(MakeRequest("alice"), "v1");
        var key2 = _cache.ComputeKey(MakeRequest("bob"), "v1");
        var key3 = _cache.ComputeKey(MakeRequest("alice", "write"), "v1");

        Assert.NotEqual(key1, key2);
        Assert.NotEqual(key1, key3);
    }

    [Fact]
    public void ComputeKey_SameInputs_SameKey()
    {
        var key1 = _cache.ComputeKey(MakeRequest("alice"), "v1");
        var key2 = _cache.ComputeKey(MakeRequest("alice"), "v1");

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void ComputeKey_DifferentPolicyVersion_DifferentKey()
    {
        var key1 = _cache.ComputeKey(MakeRequest(), "v1");
        var key2 = _cache.ComputeKey(MakeRequest(), "v2");

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void SetAndGet_ReturnsCachedResult()
    {
        var key = _cache.ComputeKey(MakeRequest(), "v1");
        var result = MakeResult();

        _cache.Set(key, result, TimeSpan.FromMinutes(5));

        Assert.True(_cache.TryGet(key, out var cached));
        Assert.Equal(result.DecisionId, cached!.DecisionId);
    }

    [Fact]
    public void InvalidateAll_ClearsAllEntries()
    {
        var key1 = "key-1";
        var key2 = "key-2";

        _cache.Set(key1, MakeResult(), TimeSpan.FromMinutes(5));
        _cache.Set(key2, MakeResult(), TimeSpan.FromMinutes(5));

        _cache.InvalidateAll();

        Assert.False(_cache.TryGet(key1, out _));
        Assert.False(_cache.TryGet(key2, out _));
    }

    [Fact]
    public void InvalidateByPolicySet_ClearsOnlyMatchingEntries()
    {
        var result1 = new EvaluationResult
        {
            DecisionId = "d1",
            Decision = Decision.Permit,
            AppliedPolicies = ["policy-set:alpha"]
        };
        var result2 = new EvaluationResult
        {
            DecisionId = "d2",
            Decision = Decision.Deny,
            AppliedPolicies = ["policy-set:beta"]
        };

        _cache.Set("key-alpha", result1, TimeSpan.FromMinutes(5));
        _cache.Set("key-beta", result2, TimeSpan.FromMinutes(5));

        _cache.InvalidateByPolicySet("alpha");

        Assert.False(_cache.TryGet("key-alpha", out _));
        Assert.True(_cache.TryGet("key-beta", out _));
    }

    [Fact]
    public void TryGet_ExpiredEntry_ReturnsFalse()
    {
        var key = "expired-key";
        _cache.Set(key, MakeResult(), TimeSpan.FromMilliseconds(1));

        Thread.Sleep(50);

        Assert.False(_cache.TryGet(key, out _));
    }

    [Fact]
    public void TryGet_NonExistent_ReturnsFalse()
    {
        Assert.False(_cache.TryGet("does-not-exist", out _));
    }
}
