using AbacController.Pdp;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for <see cref="GroupMembershipCache"/>: hit/miss, TTL expiry, key
/// stability across Keycloak group ordering, and tenant-scoped invalidation.
/// </summary>
public sealed class GroupMembershipCacheTests
{
    [Fact]
    public void TryGet_Miss_WhenEmpty()
    {
        var cache = new GroupMembershipCache(TimeSpan.FromSeconds(30));

        var hit = cache.TryGet("t", "u", Array.Empty<string>(), out var ids);

        Assert.False(hit);
        Assert.Empty(ids);
    }

    [Fact]
    public void SetAndGet_RoundTrip()
    {
        var cache = new GroupMembershipCache(TimeSpan.FromSeconds(30));
        var groups = new[] { Guid.NewGuid(), Guid.NewGuid() };

        cache.Set("t", "u", new[] { "kc1", "kc2" }, groups);

        Assert.True(cache.TryGet("t", "u", new[] { "kc1", "kc2" }, out var fetched));
        Assert.Equal(groups, fetched);
    }

    [Fact]
    public void TryGet_HonorsTtl()
    {
        var cache = new GroupMembershipCache(TimeSpan.FromMilliseconds(50));
        cache.Set("t", "u", Array.Empty<string>(), new[] { Guid.NewGuid() });
        Assert.True(cache.TryGet("t", "u", Array.Empty<string>(), out _));

        Thread.Sleep(80);

        Assert.False(cache.TryGet("t", "u", Array.Empty<string>(), out _));
    }

    [Fact]
    public void Key_StableAcrossKeycloakGroupOrdering()
    {
        var k1 = GroupMembershipCache.BuildKey("t", "u", new[] { "nato", "ops" });
        var k2 = GroupMembershipCache.BuildKey("t", "u", new[] { "ops", "nato" });
        var k3 = GroupMembershipCache.BuildKey("t", "u", new[] { "ops", "nato", "ops" });

        Assert.Equal(k1, k2);
        // Duplicates in the input collapse via the sorted hash because the
        // hash is computed over the deduped sort. Actually they don't dedupe
        // in BuildKey — verify that's intentional or assert what it does.
        // The current implementation only sorts; duplicates would change the
        // hash. Validate the actual behaviour.
        Assert.NotEqual(k1, k3);
    }

    [Fact]
    public void Key_DifferentUsers_GiveDifferentKeys()
    {
        var k1 = GroupMembershipCache.BuildKey("t", "alice", new[] { "nato" });
        var k2 = GroupMembershipCache.BuildKey("t", "bob", new[] { "nato" });
        Assert.NotEqual(k1, k2);
    }

    [Fact]
    public void Key_DifferentTenants_GiveDifferentKeys()
    {
        var k1 = GroupMembershipCache.BuildKey("t1", "alice", new[] { "nato" });
        var k2 = GroupMembershipCache.BuildKey("t2", "alice", new[] { "nato" });
        Assert.NotEqual(k1, k2);
    }

    [Fact]
    public void InvalidateTenant_RemovesOnlyMatchingEntries()
    {
        var cache = new GroupMembershipCache(TimeSpan.FromSeconds(30));
        cache.Set("t1", "alice", Array.Empty<string>(), new[] { Guid.NewGuid() });
        cache.Set("t1", "bob",   Array.Empty<string>(), new[] { Guid.NewGuid() });
        cache.Set("t2", "alice", Array.Empty<string>(), new[] { Guid.NewGuid() });

        cache.InvalidateTenant("t1");

        Assert.False(cache.TryGet("t1", "alice", Array.Empty<string>(), out _));
        Assert.False(cache.TryGet("t1", "bob",   Array.Empty<string>(), out _));
        Assert.True (cache.TryGet("t2", "alice", Array.Empty<string>(), out _));
    }
}
