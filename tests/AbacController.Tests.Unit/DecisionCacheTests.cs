using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;
using AbacController.Pdp;
using Microsoft.Extensions.Caching.Memory;

namespace AbacController.Tests.Unit;

public sealed class DecisionCacheTests
{
    private static EvaluationRequest CreateRequest(string subjectId = "user-1", string resourceId = "doc-1") => new()
    {
        RequestId = Guid.NewGuid().ToString("N"),
        Subject = new SubjectInfo { Id = subjectId, Type = "user", Properties = new Dictionary<string, object?>() },
        Action = new ActionInfo { Name = "read", Properties = new Dictionary<string, object?>() },
        Resource = new ResourceInfo { Id = resourceId, Type = "document", Properties = new Dictionary<string, object?>() }
    };

    private static EvaluationResult CreateResult(params string[] appliedPolicies) => new()
    {
        DecisionId = Guid.NewGuid().ToString("N"),
        Decision = Decision.Permit,
        AppliedPolicies = appliedPolicies.ToList(),
        Obligations = [],
        Advice = [],
        EvaluationTime = TimeSpan.FromMilliseconds(5),
        CacheStatus = "MISS",
        AttributeProvenance = []
    };

    [Fact]
    public void ComputeKey_SameRequestAndVersion_IsStable()
    {
        var cache = new DecisionCache(new MemoryCache(new MemoryCacheOptions()));
        var request = CreateRequest();

        var key1 = cache.ComputeKey(request, "v1");
        var key2 = cache.ComputeKey(request, "v1");

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void ComputeKey_DifferentInputs_ProduceDifferentKeys()
    {
        var cache = new DecisionCache(new MemoryCache(new MemoryCacheOptions()));

        var key1 = cache.ComputeKey(CreateRequest(subjectId: "user-1"), "v1");
        var key2 = cache.ComputeKey(CreateRequest(subjectId: "user-2"), "v1");
        var key3 = cache.ComputeKey(CreateRequest(subjectId: "user-1"), "v2");

        Assert.NotEqual(key1, key2);
        Assert.NotEqual(key1, key3);
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsCachedResult()
    {
        var cache = new DecisionCache(new MemoryCache(new MemoryCacheOptions()));
        var key = "abc";
        var result = CreateResult("policy:set");

        cache.Set(key, result, TimeSpan.FromMinutes(1));

        var found = cache.TryGet(key, out var cached);
        Assert.True(found);
        Assert.NotNull(cached);
        Assert.Equal(result.DecisionId, cached!.DecisionId);
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        var cache = new DecisionCache(new MemoryCache(new MemoryCacheOptions()));
        Assert.False(cache.TryGet("missing", out _));
    }

    [Fact]
    public void InvalidateByPolicySet_RemovesIndexedEntries()
    {
        var cache = new DecisionCache(new MemoryCache(new MemoryCacheOptions()));
        cache.Set("k1", CreateResult("policy-set:ps1"), TimeSpan.FromMinutes(1));
        cache.Set("k2", CreateResult("policy-set:ps2"), TimeSpan.FromMinutes(1));

        cache.InvalidateByPolicySet("ps1");

        Assert.False(cache.TryGet("k1", out _));
        Assert.True(cache.TryGet("k2", out _));
    }

    [Fact]
    public void InvalidateByPolicySet_AcceptsPrefixedKey()
    {
        var cache = new DecisionCache(new MemoryCache(new MemoryCacheOptions()));
        cache.Set("k1", CreateResult("policy-set:ps1"), TimeSpan.FromMinutes(1));

        cache.InvalidateByPolicySet("policy-set:ps1");

        Assert.False(cache.TryGet("k1", out _));
    }

    [Fact]
    public void InvalidateAll_RemovesAllIndexedEntries()
    {
        var cache = new DecisionCache(new MemoryCache(new MemoryCacheOptions()));
        cache.Set("k1", CreateResult("policy-set:ps1"), TimeSpan.FromMinutes(1));
        cache.Set("k2", CreateResult("policy-set:ps2"), TimeSpan.FromMinutes(1));

        cache.InvalidateAll();

        Assert.False(cache.TryGet("k1", out _));
        Assert.False(cache.TryGet("k2", out _));
    }

    [Fact]
    public void InvalidateByPolicySet_NonIndexedEntry_DoesNothing()
    {
        var cache = new DecisionCache(new MemoryCache(new MemoryCacheOptions()));
        cache.Set("k1", CreateResult("policy:plain"), TimeSpan.FromMinutes(1));

        cache.InvalidateByPolicySet("ps1");

        Assert.True(cache.TryGet("k1", out _));
    }
}
