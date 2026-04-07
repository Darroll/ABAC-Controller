using AbacController.Core.Domain.Entitlements;
using AbacController.Data;
using AbacController.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

/// <summary>
/// CRUD edge cases for <see cref="EntitlementRepository"/> that aren't covered by the
/// resolver behaviour tests in <see cref="EntitlementResolverTests"/>: missing-row deletes,
/// tenant isolation across direct queries, system-wide grants visible to every tenant,
/// and the (Grant + Deny) precedence return shape from GetUserOverridesAsync.
/// </summary>
public sealed class EntitlementRepositoryTests : IDisposable
{
    private const string PolicyA = "1.2.3.4";
    private readonly AbacDbContext _db;
    private readonly EntitlementRepository _repo;

    public EntitlementRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _repo = new EntitlementRepository(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task RemoveBaseline_ReturnsFalse_WhenRowMissing()
    {
        var removed = await _repo.RemoveBaselineAsync("tenant-a", "no-such-policy", null);
        Assert.False(removed);
    }

    [Fact]
    public async Task RemoveGroupEntitlement_ReturnsFalse_WhenRowMissing()
    {
        var removed = await _repo.RemoveGroupEntitlementAsync("tenant-a", "g", "missing-oid", 1);
        Assert.False(removed);
    }

    [Fact]
    public async Task RemoveUserOverride_ReturnsFalse_WhenRowMissing()
    {
        var removed = await _repo.RemoveUserOverrideAsync("tenant-a", "u", "missing-oid", null);
        Assert.False(removed);
    }

    [Fact]
    public async Task GetBaseline_ScopedToTenant_AndIncludesSystemRows()
    {
        await _repo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = "tenant-a",
            Scope = EntitlementScope.Baseline,
            PolicyOid = PolicyA
        });
        await _repo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = null, // system-wide
            Scope = EntitlementScope.Baseline,
            PolicyOid = "system.policy"
        });
        await _repo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = "tenant-other",
            Scope = EntitlementScope.Baseline,
            PolicyOid = "other.policy"
        });

        var rows = await _repo.GetBaselineAsync("tenant-a");

        Assert.Equal(2, rows.Count); // own + system, NOT other tenant
        Assert.Contains(rows, r => r.PolicyOid == PolicyA);
        Assert.Contains(rows, r => r.PolicyOid == "system.policy");
    }

    [Fact]
    public async Task AddGroupEntitlement_RequiresTargetId()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.AddGroupEntitlementAsync(new EntitlementGrant
            {
                TenantId = "tenant-a",
                Scope = EntitlementScope.Group,
                TargetId = null,
                PolicyOid = PolicyA
            }));
    }

    [Fact]
    public async Task AddUserGrant_RequiresTargetId()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.AddUserGrantAsync(new EntitlementGrant
            {
                TenantId = "tenant-a",
                Scope = EntitlementScope.User,
                TargetId = null,
                PolicyOid = PolicyA
            }));
    }

    [Fact]
    public async Task GetUserOverrides_SeparatesGrantsAndDenies()
    {
        await _repo.AddUserGrantAsync(new EntitlementGrant
        {
            TenantId = "t", Scope = EntitlementScope.User, TargetId = "u",
            PolicyOid = PolicyA, ClassificationLacv = 1
        });
        await _repo.AddUserDenyAsync(new EntitlementDeny
        {
            TenantId = "t", UserId = "u",
            PolicyOid = PolicyA, ClassificationLacv = 5, Reason = "blocked"
        });
        await _repo.AddUserDenyAsync(new EntitlementDeny
        {
            TenantId = "t", UserId = "u",
            PolicyOid = "other.policy" // whole-policy deny
        });

        var (grants, denies) = await _repo.GetUserOverridesAsync("t", "u");

        Assert.Single(grants);
        Assert.Equal(2, denies.Count);
        Assert.Contains(denies, d => d.ClassificationLacv == 5 && d.Reason == "blocked");
        Assert.Contains(denies, d => d.ClassificationLacv is null);
    }

    [Fact]
    public async Task GetAllGroupEntitlements_EmptyGroupList_ReturnsEmpty()
    {
        await _repo.AddGroupEntitlementAsync(new EntitlementGrant
        {
            TenantId = "t", Scope = EntitlementScope.Group, TargetId = "g",
            PolicyOid = PolicyA
        });

        var rows = await _repo.GetAllGroupEntitlementsAsync("t", Array.Empty<string>());
        Assert.Empty(rows);
    }

    [Fact]
    public async Task RemoveUserOverride_RemovesBothGrantAndDenyForSameKey()
    {
        await _repo.AddUserGrantAsync(new EntitlementGrant
        {
            TenantId = "t", Scope = EntitlementScope.User, TargetId = "u",
            PolicyOid = PolicyA, ClassificationLacv = 2
        });
        await _repo.AddUserDenyAsync(new EntitlementDeny
        {
            TenantId = "t", UserId = "u",
            PolicyOid = PolicyA, ClassificationLacv = 2
        });

        var removed = await _repo.RemoveUserOverrideAsync("t", "u", PolicyA, 2);

        Assert.True(removed);
        var (grants, denies) = await _repo.GetUserOverridesAsync("t", "u");
        Assert.Empty(grants);
        Assert.Empty(denies);
    }
}
