using System.Collections.Immutable;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Classifications;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Entitlements;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Policy;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Repositories;
using AbacController.Pap;
using AbacController.Pdp;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

/// <summary>
/// End-to-end engine tests exercising the Layer 2 entitlement filter that was added
/// for the Email Classification × ABAC Controller integration. Uses a real
/// <see cref="EntitlementResolver"/> backed by an in-memory SQLite
/// <see cref="AbacDbContext"/> so grants/denies round-trip through persistence.
/// </summary>
public sealed class ClassificationQueryEngineEntitlementTests : IDisposable
{
    private const string PolicyOid = "1.2.3.4";
    private const string TenantId = "tenant-a";

    private readonly AbacDbContext _db;
    private readonly EntitlementRepository _repo;
    private readonly EntitlementResolver _resolver;
    private readonly SpifParser _parser = new();

    public ClassificationQueryEngineEntitlementTests()
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

    private ISpifIndex BuildSpifIndex()
    {
        var result = _parser.Parse(TestSpifSamples.BasicPolicy);
        Assert.True(result.Success);
        return new SpifIndex(result.Spif!);
    }

    private ClassificationQueryEngine BuildEngine(ISpifIndex spifIndex)
    {
        return new ClassificationQueryEngine(
            new StubRegistry(spifIndex),
            new AcdfEvaluator(),
            new StubPolicyRepository(),
            new StubApplicationRepository(),
            new StubAuditWriter(),
            new StubTenantContext(TenantId),
            _resolver);
    }

    private static SubjectInfo MakeSubject(SecurityClearance clearance, params string[] groups)
    {
        var props = new Dictionary<string, object?> { ["securityClearance"] = clearance };
        if (groups.Length > 0) props["groups"] = groups.ToList();
        return new SubjectInfo { Type = "user", Id = "user-1", Properties = props };
    }

    private static SecurityClearance MakeClearance(params int[] lacvs) => new()
    {
        PolicyOid = PolicyOid,
        ClassificationLacvs = lacvs.Select(v => (LacvValue)v).ToImmutableHashSet()
    };

    [Fact]
    public async Task EnforceEntitlementsFalse_BehavesLikeBefore_WithoutResolver()
    {
        var engine = BuildEngine(BuildSpifIndex());
        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(MakeClearance(2, 3)),
            IncludeCategories = false,
            EnforceEntitlements = false
        };

        var result = await engine.EvaluateAsync(query);

        // With entitlements OFF, all 2 cleared classifications come through.
        Assert.Equal(2, result.Classifications.Count);
    }

    [Fact]
    public async Task EnforceEntitlementsTrue_EmptyStore_ReturnsNothing()
    {
        var engine = BuildEngine(BuildSpifIndex());
        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(MakeClearance(2, 3)),
            IncludeCategories = false,
            IncludeTrace = true,
            EnforceEntitlements = true
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Empty(result.Classifications);
        Assert.NotNull(result.Trace);
        Assert.Contains(result.Trace!.Steps, s =>
            s.FilterLayer == "entitlement_filter" && !s.Passed);
    }

    [Fact]
    public async Task BaselineGrant_AllowsClearedClassifications()
    {
        // Baseline grant covering the whole policy
        await _repo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = TenantId,
            Scope = EntitlementScope.Baseline,
            PolicyOid = PolicyOid
        });

        var engine = BuildEngine(BuildSpifIndex());
        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(MakeClearance(2, 3)),
            IncludeCategories = false,
            EnforceEntitlements = true
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Equal(2, result.Classifications.Count);
    }

    [Fact]
    public async Task UserDeny_NarrowsBaselineGrant()
    {
        await _repo.AddBaselineAsync(new EntitlementGrant
        {
            TenantId = TenantId,
            Scope = EntitlementScope.Baseline,
            PolicyOid = PolicyOid
        });
        await _repo.AddUserDenyAsync(new EntitlementDeny
        {
            TenantId = TenantId,
            UserId = "user-1",
            PolicyOid = PolicyOid,
            ClassificationLacv = 3, // SECRET
            Reason = "not authorized"
        });

        var engine = BuildEngine(BuildSpifIndex());
        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(MakeClearance(2, 3)),
            IncludeCategories = false,
            IncludeTrace = true,
            EnforceEntitlements = true
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Single(result.Classifications);
        Assert.Equal("CONFIDENTIAL", result.Classifications[0].Name);

        var denySteps = result.Trace!.Steps
            .Where(s => s.FilterLayer == "entitlement_filter" && !s.Passed)
            .ToList();
        Assert.NotEmpty(denySteps);
    }

    [Fact]
    public async Task GroupGrant_AppliesWhenSubjectIsMember()
    {
        await _repo.AddGroupEntitlementAsync(new EntitlementGrant
        {
            TenantId = TenantId,
            Scope = EntitlementScope.Group,
            TargetId = "group-alpha",
            PolicyOid = PolicyOid,
            ClassificationLacv = 2 // only CONFIDENTIAL
        });

        var engine = BuildEngine(BuildSpifIndex());
        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(MakeClearance(2, 3), "group-alpha"),
            IncludeCategories = false,
            EnforceEntitlements = true
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Single(result.Classifications);
        Assert.Equal("CONFIDENTIAL", result.Classifications[0].Name);
    }

    [Fact]
    public async Task GroupGrant_IgnoredWhenSubjectNotMember()
    {
        await _repo.AddGroupEntitlementAsync(new EntitlementGrant
        {
            TenantId = TenantId,
            Scope = EntitlementScope.Group,
            TargetId = "group-alpha",
            PolicyOid = PolicyOid,
            ClassificationLacv = 2
        });

        var engine = BuildEngine(BuildSpifIndex());
        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(MakeClearance(2, 3), "group-other"),
            IncludeCategories = false,
            EnforceEntitlements = true
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Empty(result.Classifications);
    }

    // ── Minimal stubs (mirrors the private ones in ClassificationQueryEngineTests) ──

    private sealed class StubRegistry(ISpifIndex index) : ISpifRegistry
    {
        private readonly ISpifIndex _index = index;
        public ISpifIndex? GetByPolicyOid(string policyOid) => _index.PolicyOid == policyOid ? _index : null;
        public ISpifIndex? GetDefault() => _index;
        public void Register(ISpifIndex spifIndex) { }
        public void SetDefault(string policyOid) { }
        public void Remove(string policyOid) { }
        public bool IsRegistered(string policyOid) => _index.PolicyOid == policyOid;
        public IReadOnlyList<string> GetRegisteredPolicyOids() => [_index.PolicyOid];
    }

    private sealed class StubPolicyRepository : IPolicyRepository
    {
        public Task<List<PolicySet>> GetPolicySetsAsync(CancellationToken ct = default) => Task.FromResult(new List<PolicySet>());
        public Task<PolicySet?> GetPolicySetAsync(string id, CancellationToken ct = default) => Task.FromResult<PolicySet?>(null);
        public Task<PolicySet> CreatePolicySetAsync(PolicySet policySet, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PolicySet> UpdatePolicySetAsync(PolicySet policySet, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeletePolicySetAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Policy?> GetPolicyAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Policy> CreatePolicyAsync(Policy policy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Policy> UpdatePolicyAsync(Policy policy, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeletePolicyAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<PolicyVersion>> GetVersionsAsync(string policyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PolicyVersion?> GetVersionAsync(Guid versionId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PolicyVersion> CreateVersionAsync(PolicyVersion version, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ActivateVersionAsync(string policyId, Guid versionId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class StubApplicationRepository : IApplicationRepository
    {
        public Task<List<ApplicationRegistration>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(new List<ApplicationRegistration>());
        public Task<ApplicationRegistration?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<ApplicationRegistration?>(null);
        public Task<ApplicationRegistration> UpsertAsync(ApplicationRegistration registration, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class StubAuditWriter : IAuditWriter
    {
        public void Write(AuditEvent auditEvent) { }
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubTenantContext(string? tenantId) : ITenantContext
    {
        public string? TenantId { get; } = tenantId;
    }
}
