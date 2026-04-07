using AbacController.Core.Domain.Entitlements;
using AbacController.Data;
using AbacController.Data.Repositories;
using AbacController.Pdp;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

public sealed class EntitlementResolverTests : IDisposable
{
    private readonly AbacDbContext _db;
    private readonly EntitlementRepository _repo;
    private readonly EntitlementResolver _resolver;
    private const string Tenant = "tenant-a";
    private const string PolicyA = "1.2.3.4";
    private const string PolicyB = "5.6.7.8";

    public EntitlementResolverTests()
    {
        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AbacDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _repo = new EntitlementRepository(_db);
        _resolver = new EntitlementResolver(_repo);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private EntitlementSubject Subject(params string[] groupIds) => new()
    {
        SubjectId = "user-1",
        TenantId = Tenant,
        GroupIds = groupIds
    };

    [Fact]
    public async Task Resolve_EmptyStore_ReturnsEmpty()
    {
        var result = await _resolver.ResolveAsync(Subject());
        Assert.True(result.IsEmpty);
        Assert.False(result.Permits(PolicyA, 1));
    }

    [Fact]
    public async Task Resolve_BaselinePolicyGrant_PermitsAllLacvs()
    {
        await _repo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant,
            Scope = EntitlementScope.Baseline,
            PolicyOid = PolicyA
        });

        var result = await _resolver.ResolveAsync(Subject());

        Assert.Contains(PolicyA, result.PolicyOids);
        Assert.True(result.Permits(PolicyA, 1));
        Assert.True(result.Permits(PolicyA, 99));
        Assert.False(result.Permits(PolicyB, 1));
    }

    [Fact]
    public async Task Resolve_GroupClassificationGrant_PermitsOnlyThatLacv()
    {
        await _repo.AddGroupEntitlementAsync(new EntitlementGrant
        {
            TenantId = Tenant,
            Scope = EntitlementScope.Group,
            TargetId = "group-alpha",
            PolicyOid = PolicyA,
            ClassificationLacv = 2
        });

        var result = await _resolver.ResolveAsync(Subject("group-alpha"));

        Assert.True(result.Permits(PolicyA, 2));
        Assert.False(result.Permits(PolicyA, 3));
        Assert.False(result.Permits(PolicyA, 1));
    }

    [Fact]
    public async Task Resolve_GroupGrant_IgnoredWhenSubjectNotMember()
    {
        await _repo.AddGroupEntitlementAsync(new EntitlementGrant
        {
            TenantId = Tenant,
            Scope = EntitlementScope.Group,
            TargetId = "group-alpha",
            PolicyOid = PolicyA,
            ClassificationLacv = 2
        });

        var result = await _resolver.ResolveAsync(Subject("group-other"));

        Assert.False(result.Permits(PolicyA, 2));
    }

    [Fact]
    public async Task Resolve_UserGrantAddsOnTopOfBaseline()
    {
        await _repo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Baseline,
            PolicyOid = PolicyA, ClassificationLacv = 1
        });
        await _repo.AddUserGrantAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.User,
            TargetId = "user-1",
            PolicyOid = PolicyA, ClassificationLacv = 3
        });

        var result = await _resolver.ResolveAsync(Subject());

        Assert.True(result.Permits(PolicyA, 1));
        Assert.True(result.Permits(PolicyA, 3));
        Assert.False(result.Permits(PolicyA, 2));
    }

    [Fact]
    public async Task Resolve_UserClassificationDeny_BeatsBaselinePolicyGrant()
    {
        await _repo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Baseline,
            PolicyOid = PolicyA // whole-policy
        });
        await _repo.AddUserDenyAsync(new EntitlementDeny
        {
            TenantId = Tenant, UserId = "user-1",
            PolicyOid = PolicyA, ClassificationLacv = 5,
            Reason = "too sensitive for this user"
        });

        var result = await _resolver.ResolveAsync(Subject());

        Assert.True(result.Permits(PolicyA, 1));   // still allowed
        Assert.True(result.Permits(PolicyA, 10));  // still allowed
        Assert.False(result.Permits(PolicyA, 5));  // specifically denied
    }

    [Fact]
    public async Task Resolve_UserWholePolicyDeny_BlocksEverything()
    {
        await _repo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Baseline, PolicyOid = PolicyA
        });
        await _repo.AddUserDenyAsync(new EntitlementDeny
        {
            TenantId = Tenant, UserId = "user-1", PolicyOid = PolicyA
        });

        var result = await _resolver.ResolveAsync(Subject());

        Assert.False(result.Permits(PolicyA, 1));
        Assert.False(result.Permits(PolicyA, 99));
        Assert.Contains(PolicyA, result.DeniedPolicyOids);
    }

    [Fact]
    public async Task Resolve_CombinesBaselineGroupsAndUserGrants()
    {
        await _repo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Baseline,
            PolicyOid = PolicyA, ClassificationLacv = 1
        });
        await _repo.AddGroupEntitlementAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.Group, TargetId = "g",
            PolicyOid = PolicyA, ClassificationLacv = 2
        });
        await _repo.AddUserGrantAsync(new EntitlementGrant
        {
            TenantId = Tenant, Scope = EntitlementScope.User, TargetId = "user-1",
            PolicyOid = PolicyB // whole policy
        });

        var result = await _resolver.ResolveAsync(Subject("g"));

        Assert.True(result.Permits(PolicyA, 1));
        Assert.True(result.Permits(PolicyA, 2));
        Assert.False(result.Permits(PolicyA, 3));
        Assert.True(result.Permits(PolicyB, 42));
    }

    [Fact]
    public async Task Resolve_TenantIsolation_IgnoresOtherTenants()
    {
        await _repo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = "other-tenant", Scope = EntitlementScope.Baseline, PolicyOid = PolicyA
        });

        var result = await _resolver.ResolveAsync(Subject());

        Assert.False(result.Permits(PolicyA, 1));
        Assert.True(result.IsEmpty);
    }
}
