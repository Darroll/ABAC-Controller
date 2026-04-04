using AbacController.Core.Domain.Policy;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Repositories;
using AbacController.Pap;
using AbacController.Pdp;
using AbacController.Core.Domain.Spif;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests tenant-scoped isolation across repositories and in-memory SPIF registries.
/// </summary>
public sealed class MultiTenantIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AbacDbContext _db;

    /// <summary>
    /// Initializes a new in-memory database for tenant isolation tests.
    /// </summary>
    public MultiTenantIsolationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AbacDbContext(options);
        _db.Database.EnsureCreated();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task PolicyRepository_GetPolicySetsAsync_ReturnsOnlyCurrentTenantRows()
    {
        var tenantA = CreateRepository("tenant-a");
        var tenantB = CreateRepository("tenant-b");
        var defaultTenant = CreateRepository(null);

        await tenantA.CreatePolicySetAsync(new PolicySet { Id = "ps-a", Name = "Tenant A" });
        await tenantB.CreatePolicySetAsync(new PolicySet { Id = "ps-b", Name = "Tenant B" });
        await defaultTenant.CreatePolicySetAsync(new PolicySet { Id = "ps-default", Name = "Default" });

        var aSets = await tenantA.GetPolicySetsAsync();
        var bSets = await tenantB.GetPolicySetsAsync();
        var defaultSets = await defaultTenant.GetPolicySetsAsync();

        Assert.Single(aSets);
        Assert.Equal("ps-a", aSets[0].Id);
        Assert.Single(bSets);
        Assert.Equal("ps-b", bSets[0].Id);
        Assert.Single(defaultSets);
        Assert.Equal("ps-default", defaultSets[0].Id);
    }

    [Fact]
    public async Task PolicyRepository_GetPolicyAsync_DoesNotCrossTenantBoundary()
    {
        var tenantA = CreateRepository("tenant-a");
        var tenantB = CreateRepository("tenant-b");

        await tenantA.CreatePolicySetAsync(new PolicySet { Id = "ps-a", Name = "Tenant A" });
        await tenantA.CreatePolicyAsync(new Policy { Id = "policy-a", PolicySetId = "ps-a", Name = "Policy A" });

        var fromTenantA = await tenantA.GetPolicyAsync("policy-a");
        var fromTenantB = await tenantB.GetPolicyAsync("policy-a");

        Assert.NotNull(fromTenantA);
        Assert.Null(fromTenantB);
    }

    [Fact]
    public async Task PolicyRepository_DeletePolicySet_DoesNotAffectOtherTenantRows()
    {
        var tenantA = CreateRepository("tenant-a");
        var tenantB = CreateRepository("tenant-b");

        await tenantA.CreatePolicySetAsync(new PolicySet { Id = "tenant-a-set", Name = "Tenant A" });
        await tenantB.CreatePolicySetAsync(new PolicySet { Id = "tenant-b-set", Name = "Tenant B" });

        await tenantA.DeletePolicySetAsync("tenant-a-set");

        Assert.Null(await tenantA.GetPolicySetAsync("tenant-a-set"));
        Assert.NotNull(await tenantB.GetPolicySetAsync("tenant-b-set"));
    }

    [Fact]
    public void TenantSpifRegistry_SeparatesRegistrationsPerTenant()
    {
        var store = new TenantSpifRegistryStore();
        var tenantARegistry = new TenantSpifRegistry(store, new TestTenantContext("tenant-a"));
        var tenantBRegistry = new TenantSpifRegistry(store, new TestTenantContext("tenant-b"));
        var defaultRegistry = new TenantSpifRegistry(store, new TestTenantContext(null));

        tenantARegistry.Register(MakeSpifIndex("1.2.3.4", "Tenant A SPIF"));
        tenantBRegistry.Register(MakeSpifIndex("5.6.7.8", "Tenant B SPIF"));
        defaultRegistry.Register(MakeSpifIndex("9.9.9.9", "Default SPIF"));

        Assert.NotNull(tenantARegistry.GetByPolicyOid("1.2.3.4"));
        Assert.Null(tenantARegistry.GetByPolicyOid("5.6.7.8"));
        Assert.Null(tenantARegistry.GetByPolicyOid("9.9.9.9"));

        Assert.NotNull(tenantBRegistry.GetByPolicyOid("5.6.7.8"));
        Assert.Null(tenantBRegistry.GetByPolicyOid("1.2.3.4"));
        Assert.Null(tenantBRegistry.GetByPolicyOid("9.9.9.9"));

        Assert.NotNull(defaultRegistry.GetByPolicyOid("9.9.9.9"));
        Assert.Null(defaultRegistry.GetByPolicyOid("1.2.3.4"));
        Assert.Null(defaultRegistry.GetByPolicyOid("5.6.7.8"));
    }

    [Fact]
    public void TenantSpifRegistry_DefaultsAreTenantScoped()
    {
        var store = new TenantSpifRegistryStore();
        var tenantARegistry = new TenantSpifRegistry(store, new TestTenantContext("tenant-a"));
        var tenantBRegistry = new TenantSpifRegistry(store, new TestTenantContext("tenant-b"));

        tenantARegistry.Register(MakeSpifIndex("1.2.3.4", "Tenant A Default"));
        tenantBRegistry.Register(MakeSpifIndex("5.6.7.8", "Tenant B Default"));

        Assert.Equal("1.2.3.4", tenantARegistry.GetDefault()!.PolicyOid);
        Assert.Equal("5.6.7.8", tenantBRegistry.GetDefault()!.PolicyOid);
    }

    private PolicyRepository CreateRepository(string? tenantId)
        => new(_db, new TestTenantContext(tenantId));

    private static ISpifIndex MakeSpifIndex(string oid, string name)
    {
        var spif = new Spif
        {
            SchemaVersion = "2.1",
            PolicyId = new PolicyInfo { Name = name, Oid = oid },
            Classifications = [],
            CategoryTagSets = [],
            EquivalentPolicies = []
        };

        return new SpifIndex(spif);
    }

    private sealed record TestTenantContext(string? TenantId) : ITenantContext;
}
