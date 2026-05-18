using System.Collections.Immutable;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Classifications;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Entitlements;
using AbacController.Core.Domain.Groups;
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
/// Engine-level tests for the new resolver-aware path added in A8: when
/// EnforceEntitlements is true and the caller does NOT push group ids in
/// subject.Properties, the engine resolves the effective ABAC group set via
/// IGroupMembershipResolver + IKeycloakGroupClaimsProvider.
///
/// Caller-pushed groups must still win when present, to keep the existing
/// Phase A entitlement tests green.
/// </summary>
public sealed class ClassificationQueryEngineResolverTests : IDisposable
{
    private const string PolicyOid = "1.2.3.4";
    private const string TenantId = "tenant-a";

    private readonly AbacDbContext _db;
    private readonly EntitlementRepository _entitlementRepo;
    private readonly EntitlementResolver _entitlementResolver;
    private readonly AbacGroupRepository _groupRepo;
    private readonly GroupMembershipCache _cache = new(TimeSpan.FromMinutes(5));
    private readonly GroupMembershipResolver _groupResolver;
    private readonly StubKeycloakClaims _keycloakClaims = new();
    private readonly SpifParser _parser = new();

    public ClassificationQueryEngineResolverTests()
    {
        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _entitlementRepo = new EntitlementRepository(_db);
        _entitlementResolver = new EntitlementResolver(_entitlementRepo);
        _groupRepo = new AbacGroupRepository(_db);
        _groupResolver = new GroupMembershipResolver(_groupRepo, _cache);
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
        => new(
            new StubRegistry(spifIndex),
            new AcdfEvaluator(),
            new StubPolicyRepository(),
            new StubApplicationRepository(),
            new StubAuditWriter(),
            new StubTenantContext(TenantId),
            _entitlementResolver,
            _groupResolver,
            _keycloakClaims);

    private static SubjectInfo MakeSubject(SecurityClearance clearance, params string[] callerGroups)
    {
        var props = new Dictionary<string, object?> { ["securityClearance"] = clearance };
        if (callerGroups.Length > 0) props["groups"] = callerGroups.ToList();
        return new SubjectInfo { Type = "user", Id = "alice", Properties = props };
    }

    private static SecurityClearance MakeClearance(params int[] lacvs) => new()
    {
        PolicyOid = PolicyOid,
        ClassificationLacvs = lacvs.Select(v => (LacvValue)v).ToImmutableHashSet()
    };

    [Fact]
    public async Task ResolverPath_ResolvesGroups_WhenCallerDoesNotPushThem()
    {
        // Set up: an ABAC group with Alice as a direct member, and a group-level
        // entitlement on PolicyA classification SECRET.
        var nato = await _groupRepo.CreateGroupAsync(new AbacGroup
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Name = "nato"
        });
        await _groupRepo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = nato.Id, Kind = AbacGroupMemberKind.User, MemberId = "alice", TenantId = TenantId
        });
        await _entitlementRepo.AddGroupEntitlementAsync(new EntitlementGrant
        {
            TenantId = TenantId,
            Scope = EntitlementScope.Group,
            TargetId = nato.Id.ToString(),
            PolicyOid = PolicyOid,
            ClassificationLacv = 3 // SECRET
        });

        var engine = BuildEngine(BuildSpifIndex());
        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(MakeClearance(2, 3)),  // no groups pushed
            EnforceEntitlements = true,
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        // Resolver should have found Alice in the nato group, fed that into
        // the entitlement resolver, which permits SECRET on the policy.
        Assert.Single(result.Classifications);
        Assert.Equal("SECRET", result.Classifications[0].Name);
    }

    [Fact]
    public async Task ResolverPath_HonorsKeycloakGroupClaims()
    {
        var nato = await _groupRepo.CreateGroupAsync(new AbacGroup
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Name = "nato"
        });
        await _groupRepo.AddMemberAsync(new AbacGroupMembership
        {
            AbacGroupId = nato.Id,
            Kind = AbacGroupMemberKind.KeycloakGroup,
            MemberId = "kc-nato",
            TenantId = TenantId
        });
        await _entitlementRepo.AddGroupEntitlementAsync(new EntitlementGrant
        {
            TenantId = TenantId,
            Scope = EntitlementScope.Group,
            TargetId = nato.Id.ToString(),
            PolicyOid = PolicyOid
        });

        _keycloakClaims.SetGroups(new[] { "kc-nato" });

        var engine = BuildEngine(BuildSpifIndex());
        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(MakeClearance(2, 3)),
            EnforceEntitlements = true,
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Equal(2, result.Classifications.Count);
    }

    [Fact]
    public async Task ResolverPath_NoOps_WhenCallerPushesGroups()
    {
        // Caller-pushed groups must still win — the resolver path is a
        // back-compat shim, not a replacement.
        await _entitlementRepo.AddGroupEntitlementAsync(new EntitlementGrant
        {
            TenantId = TenantId,
            Scope = EntitlementScope.Group,
            TargetId = "caller-supplied-group",
            PolicyOid = PolicyOid
        });

        var engine = BuildEngine(BuildSpifIndex());
        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(MakeClearance(2, 3), callerGroups: new[] { "caller-supplied-group" }),
            EnforceEntitlements = true,
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        // The "caller-supplied-group" string is not a Guid (an ABAC group id),
        // but the existing entitlement model accepts opaque group ids. So the
        // entitlement resolves and the policy is visible.
        Assert.Equal(2, result.Classifications.Count);
    }

    [Fact]
    public async Task ResolverPath_ReturnsEmpty_WhenSubjectInNoGroupsAndNoBaseline()
    {
        var engine = BuildEngine(BuildSpifIndex());
        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(MakeClearance(2, 3)),
            EnforceEntitlements = true,
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);
        Assert.Empty(result.Classifications);
    }

    // ── Stubs (private to this test file) ──

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

    private sealed class StubKeycloakClaims : IKeycloakGroupClaimsProvider
    {
        public IReadOnlyCollection<string> KeycloakGroupIds { get; private set; } = Array.Empty<string>();
        public void SetGroups(IReadOnlyCollection<string> groups) => KeycloakGroupIds = groups ?? Array.Empty<string>();
    }
}
