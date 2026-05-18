using AbacController.Core.Domain.Groups;
using AbacController.Data;
using AbacController.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for the ABAC group repository covering CRUD, idempotent membership
/// inserts, tenant isolation, system-wide groups, and the hot-path
/// <see cref="AbacGroupRepository.ResolveAbacGroupIdsAsync"/> resolver in all
/// four shapes (direct only, inherited only, mixed, empty).
/// </summary>
public sealed class AbacGroupRepositoryTests : IDisposable
{
    private const string Tenant = "tenant-a";

    private readonly AbacDbContext _db;
    private readonly AbacGroupRepository _repo;

    public AbacGroupRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _repo = new AbacGroupRepository(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private async Task<AbacGroup> SeedGroup(string name, string? tenant = Tenant) =>
        await _repo.CreateGroupAsync(new AbacGroup
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            Name = name
        });

    [Fact]
    public async Task CreateAndGet_RoundTripsRow()
    {
        var created = await SeedGroup("nato-readers");

        var fetched = await _repo.GetGroupAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("nato-readers", fetched!.Name);
        Assert.Equal(Tenant, fetched.TenantId);
    }

    [Fact]
    public async Task ListGroups_TenantScoped_AndIncludesSystemWide()
    {
        await SeedGroup("tenant-only", tenant: Tenant);
        await SeedGroup("other-tenant", tenant: "tenant-other");
        await SeedGroup("global", tenant: null);

        var rows = await _repo.ListGroupsAsync(Tenant);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, g => g.Name == "tenant-only");
        Assert.Contains(rows, g => g.Name == "global");
    }

    [Fact]
    public async Task CreateGroup_RejectsDuplicateName_PerTenant()
    {
        await SeedGroup("duplicate");
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => SeedGroup("duplicate"));
    }

    [Fact]
    public async Task DeleteGroup_RemovesGroupAndMemberships()
    {
        var group = await SeedGroup("doomed");
        await _repo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = group.Id,
            Kind = AbacGroupMemberKind.User,
            MemberId = "alice",
            TenantId = Tenant
        });

        var deleted = await _repo.DeleteGroupAsync(group.Id);
        Assert.True(deleted);

        Assert.Null(await _repo.GetGroupAsync(group.Id));
        Assert.Empty(await _repo.ListMembersAsync(group.Id));
    }

    [Fact]
    public async Task DeleteGroup_NotFound_ReturnsFalse()
    {
        var deleted = await _repo.DeleteGroupAsync(Guid.NewGuid());
        Assert.False(deleted);
    }

    [Fact]
    public async Task AddMember_IsIdempotent()
    {
        var group = await SeedGroup("g");

        var first = await _repo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = group.Id,
            Kind = AbacGroupMemberKind.User,
            MemberId = "alice",
            TenantId = Tenant
        });
        var second = await _repo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = group.Id,
            Kind = AbacGroupMemberKind.User,
            MemberId = "alice",
            TenantId = Tenant
        });

        Assert.Equal(first.AbacGroupId, second.AbacGroupId);
        Assert.Single(await _repo.ListMembersAsync(group.Id));
    }

    [Fact]
    public async Task RemoveMember_NotFound_ReturnsFalse()
    {
        var group = await SeedGroup("g");
        var removed = await _repo.RemoveMemberAsync(group.Id, AbacGroupMemberKind.User, "ghost");
        Assert.False(removed);
    }

    [Fact]
    public async Task RemoveMember_DeletesRow()
    {
        var group = await SeedGroup("g");
        await _repo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = group.Id, Kind = AbacGroupMemberKind.User, MemberId = "alice", TenantId = Tenant
        });

        var removed = await _repo.RemoveMemberAsync(group.Id, AbacGroupMemberKind.User, "alice");

        Assert.True(removed);
        Assert.Empty(await _repo.ListMembersAsync(group.Id));
    }

    // ── Resolver hot path ───────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_DirectMembershipOnly()
    {
        var nato = await SeedGroup("nato");
        var dod = await SeedGroup("dod");
        await _repo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = nato.Id, Kind = AbacGroupMemberKind.User, MemberId = "alice", TenantId = Tenant
        });

        var ids = await _repo.ResolveAbacGroupIdsAsync(Tenant, "alice", Array.Empty<string>());

        Assert.Single(ids);
        Assert.Contains(nato.Id, ids);
        Assert.DoesNotContain(dod.Id, ids);
    }

    [Fact]
    public async Task Resolve_InheritedKeycloakGroupOnly()
    {
        var nato = await SeedGroup("nato");
        await _repo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = nato.Id,
            Kind = AbacGroupMemberKind.KeycloakGroup,
            MemberId = "kc-nato",
            TenantId = Tenant
        });

        var ids = await _repo.ResolveAbacGroupIdsAsync(Tenant, userId: "", new[] { "kc-nato" });

        Assert.Single(ids);
        Assert.Contains(nato.Id, ids);
    }

    [Fact]
    public async Task Resolve_DirectAndInheritedUnion()
    {
        var nato = await SeedGroup("nato");
        var ops = await SeedGroup("ops");
        await _repo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = nato.Id,
            Kind = AbacGroupMemberKind.KeycloakGroup,
            MemberId = "kc-nato",
            TenantId = Tenant
        });
        await _repo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = ops.Id,
            Kind = AbacGroupMemberKind.User,
            MemberId = "alice",
            TenantId = Tenant
        });

        var ids = await _repo.ResolveAbacGroupIdsAsync(Tenant, "alice", new[] { "kc-nato", "kc-other" });

        Assert.Equal(2, ids.Count);
        Assert.Contains(nato.Id, ids);
        Assert.Contains(ops.Id, ids);
    }

    [Fact]
    public async Task Resolve_DistinctsAcrossOverlap()
    {
        var nato = await SeedGroup("nato");
        // Same group has Alice as both a direct member and via keycloak — must
        // dedupe to a single Guid in the result.
        await _repo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = nato.Id, Kind = AbacGroupMemberKind.User, MemberId = "alice", TenantId = Tenant
        });
        await _repo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = nato.Id, Kind = AbacGroupMemberKind.KeycloakGroup, MemberId = "kc-nato", TenantId = Tenant
        });

        var ids = await _repo.ResolveAbacGroupIdsAsync(Tenant, "alice", new[] { "kc-nato" });
        Assert.Single(ids);
    }

    [Fact]
    public async Task Resolve_TenantIsolation()
    {
        var natoOther = await SeedGroup("nato-other", tenant: "tenant-other");
        await _repo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = natoOther.Id,
            Kind = AbacGroupMemberKind.User,
            MemberId = "alice",
            TenantId = "tenant-other"
        });

        var ids = await _repo.ResolveAbacGroupIdsAsync(Tenant, "alice", Array.Empty<string>());
        Assert.Empty(ids);
    }

    [Fact]
    public async Task Resolve_SystemWideGroupVisibleToEveryTenant()
    {
        var systemGroup = await SeedGroup("system-admins", tenant: null);
        await _repo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = systemGroup.Id,
            Kind = AbacGroupMemberKind.User,
            MemberId = "alice",
            TenantId = null
        });

        var ids = await _repo.ResolveAbacGroupIdsAsync(Tenant, "alice", Array.Empty<string>());
        Assert.Contains(systemGroup.Id, ids);
    }

    [Fact]
    public async Task Resolve_EmptySubject_ReturnsEmpty()
    {
        await SeedGroup("g");
        var ids = await _repo.ResolveAbacGroupIdsAsync(Tenant, "", Array.Empty<string>());
        Assert.Empty(ids);
    }

    [Fact]
    public async Task Resolve_NoMatchingMemberships_ReturnsEmpty()
    {
        await SeedGroup("g");
        var ids = await _repo.ResolveAbacGroupIdsAsync(Tenant, "ghost", new[] { "kc-ghost" });
        Assert.Empty(ids);
    }
}
