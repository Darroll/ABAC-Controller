using AbacController.Core.Domain.Groups;
using AbacController.Core.Interfaces;
using AbacController.Pdp;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for <see cref="GroupMembershipResolver"/> covering cache hit, cache
/// miss → repo call, and tenant-scoped invalidation. Uses a stub repo so the
/// tests don't need a database.
/// </summary>
public sealed class GroupMembershipResolverTests
{
    [Fact]
    public async Task Resolve_DelegatesToRepoOnCacheMiss()
    {
        var groupId = Guid.NewGuid();
        var repo = new StubRepo(_ => new[] { groupId });
        var cache = new GroupMembershipCache(TimeSpan.FromSeconds(30));
        var resolver = new GroupMembershipResolver(repo, cache);

        var ids = await resolver.ResolveAsync("t", "alice", new[] { "kc-nato" });

        Assert.Equal(new[] { groupId }, ids);
        Assert.Equal(1, repo.Calls);
    }

    [Fact]
    public async Task Resolve_HitsCacheOnSecondCall()
    {
        var groupId = Guid.NewGuid();
        var repo = new StubRepo(_ => new[] { groupId });
        var cache = new GroupMembershipCache(TimeSpan.FromSeconds(30));
        var resolver = new GroupMembershipResolver(repo, cache);

        await resolver.ResolveAsync("t", "alice", new[] { "kc" });
        await resolver.ResolveAsync("t", "alice", new[] { "kc" });

        Assert.Equal(1, repo.Calls);
    }

    [Fact]
    public async Task InvalidateTenant_ForcesRepoCall()
    {
        var repo = new StubRepo(_ => new[] { Guid.NewGuid() });
        var cache = new GroupMembershipCache(TimeSpan.FromSeconds(30));
        var resolver = new GroupMembershipResolver(repo, cache);

        await resolver.ResolveAsync("t", "alice", Array.Empty<string>());
        resolver.InvalidateTenant("t");
        await resolver.ResolveAsync("t", "alice", Array.Empty<string>());

        Assert.Equal(2, repo.Calls);
    }

    [Fact]
    public async Task InvalidateTenant_LeavesOtherTenantsCached()
    {
        var repo = new StubRepo(_ => new[] { Guid.NewGuid() });
        var cache = new GroupMembershipCache(TimeSpan.FromSeconds(30));
        var resolver = new GroupMembershipResolver(repo, cache);

        await resolver.ResolveAsync("t1", "alice", Array.Empty<string>());
        await resolver.ResolveAsync("t2", "alice", Array.Empty<string>());
        resolver.InvalidateTenant("t1");

        // t1 fires; t2 stays cached.
        await resolver.ResolveAsync("t1", "alice", Array.Empty<string>());
        await resolver.ResolveAsync("t2", "alice", Array.Empty<string>());

        Assert.Equal(3, repo.Calls);
    }

    [Fact]
    public async Task DifferentKeycloakGroupSets_HitDifferentCacheEntries()
    {
        var repo = new StubRepo(_ => new[] { Guid.NewGuid() });
        var cache = new GroupMembershipCache(TimeSpan.FromSeconds(30));
        var resolver = new GroupMembershipResolver(repo, cache);

        await resolver.ResolveAsync("t", "alice", new[] { "kc-nato" });
        await resolver.ResolveAsync("t", "alice", new[] { "kc-dod" });

        Assert.Equal(2, repo.Calls);
    }

    private sealed class StubRepo : IAbacGroupRepository
    {
        private readonly Func<int, IReadOnlyCollection<Guid>> _produce;
        public int Calls { get; private set; }

        public StubRepo(Func<int, IReadOnlyCollection<Guid>> produce)
        {
            _produce = produce;
        }

        public Task<IReadOnlyCollection<Guid>> ResolveAbacGroupIdsAsync(
            string tenantId,
            string userId,
            IReadOnlyCollection<string> keycloakGroupIds,
            CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_produce(Calls));
        }

        // Unused — only the resolver method matters here.
        public Task<List<AbacGroup>> ListGroupsAsync(string tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AbacGroup?> GetGroupAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AbacGroup> CreateGroupAsync(AbacGroup group, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteGroupAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<AbacGroupMembership>> ListMembersAsync(Guid groupId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AbacGroupMembership> AddMemberAsync(AbacGroupMembership membership, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RemoveMemberAsync(Guid groupId, AbacGroupMemberKind kind, string memberId, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
