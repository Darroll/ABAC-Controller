using System.Collections.Immutable;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Classifications;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Policy;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;
using AbacController.Pap;
using AbacController.Pdp;

namespace AbacController.Tests.Unit;

public sealed class ClassificationQueryEngineTests
{
    private readonly SpifParser _parser = new();

    private static SubjectInfo MakeSubject(SecurityClearance clearance)
    {
        return new SubjectInfo
        {
            Type = "user",
            Id = "user-1",
            Properties = new Dictionary<string, object?> { ["securityClearance"] = clearance }
        };
    }

    private static SecurityClearance MakeClearance(string policyOid, params int[] lacvs)
    {
        return new SecurityClearance
        {
            PolicyOid = policyOid,
            ClassificationLacvs = lacvs.Select(v => (LacvValue)v).ToImmutableHashSet()
        };
    }

    private ISpifIndex BuildSpifIndex(string xml = TestSpifSamples.BasicPolicy)
    {
        var result = _parser.Parse(xml);
        Assert.True(result.Success, string.Join(" | ", result.Errors.Select(e => e.Message)));
        return new SpifIndex(result.Spif!);
    }

    private ClassificationQueryEngine BuildEngine(
        ISpifIndex? spifIndex = null,
        List<ApplicationRegistration>? apps = null,
        List<PolicySet>? policySets = null)
    {
        var registry = new StubSpifRegistry(spifIndex);
        var acdf = new AcdfEvaluator();
        var policyRepo = new StubPolicyRepository(policySets ?? []);
        var appRepo = new StubApplicationRepository(apps ?? []);
        var auditWriter = new StubAuditWriter();
        var tenantContext = new StubTenantContext(null);

        return new ClassificationQueryEngine(
            registry, acdf, policyRepo, appRepo, auditWriter, tenantContext);
    }

    // ── SPIF Resolution Tests ──

    [Fact]
    public async Task Evaluate_ReturnsEmptyResult_WhenNoSpifFound()
    {
        var engine = BuildEngine(spifIndex: null);
        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(MakeClearance("1.2.3.4", 2, 3))
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Empty(result.Classifications);
        Assert.Equal(0, result.TotalSpifClassifications);
    }

    // ── ACDF Clearance Filter Tests ──

    [Fact]
    public async Task Evaluate_FiltersClassificationsByHierarchy()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = MakeClearance("1.2.3.4", 2); // CONFIDENTIAL only
        var engine = BuildEngine(spifIndex);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Single(result.Classifications);
        Assert.Equal("CONFIDENTIAL", result.Classifications[0].Name);
        Assert.Equal(2, result.TotalSpifClassifications);
    }

    [Fact]
    public async Task Evaluate_AllowsAllClassifications_WhenClearanceCoversAll()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = MakeClearance("1.2.3.4", 2, 3); // CONFIDENTIAL + SECRET
        var engine = BuildEngine(spifIndex);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Equal(2, result.Classifications.Count);
        Assert.Equal("CONFIDENTIAL", result.Classifications[0].Name);
        Assert.Equal("SECRET", result.Classifications[1].Name);
    }

    [Fact]
    public async Task Evaluate_ReturnsEmpty_WhenNoClearanceProvided()
    {
        var spifIndex = BuildSpifIndex();
        var engine = BuildEngine(spifIndex);

        var query = new ClassificationAssignmentQuery
        {
            Subject = new SubjectInfo { Type = "user", Id = "user-1" },
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Empty(result.Classifications);
    }

    [Fact]
    public async Task Evaluate_ExcludesObsoleteClassifications()
    {
        const string xml = """
<spif:SPIF xmlns:spif="http://www.xmlspif.org/spif" schemaVersion="2.1" creationDate="20260101000000Z">
  <spif:securityPolicyId name="TEST" id="1.2.3.4" />
  <spif:securityClassifications>
    <spif:securityClassification name="UNCLASSIFIED" lacv="1" hierarchy="1" />
    <spif:securityClassification name="OLD" lacv="2" hierarchy="2" obsolete="true" />
    <spif:securityClassification name="SECRET" lacv="3" hierarchy="3" />
  </spif:securityClassifications>
</spif:SPIF>
""";
        var spifIndex = BuildSpifIndex(xml);
        var clearance = MakeClearance("1.2.3.4", 1, 2, 3);
        var engine = BuildEngine(spifIndex);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Equal(2, result.Classifications.Count);
        Assert.DoesNotContain(result.Classifications, c => c.Name == "OLD");
    }

    // ── Policy Filter Tests ──

    [Fact]
    public async Task Evaluate_DeniesClassification_WhenPolicyExplicitlyDenies()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = MakeClearance("1.2.3.4", 2, 3);

        // Create a policy that denies SECRET classification assignment
        var policyContent = """
{
  "rules": [
    {
      "id": "deny-secret",
      "effect": "Deny",
      "conditions": [
        { "path": "resource.properties.classificationName", "equals": "SECRET" }
      ]
    }
  ]
}
""";
        var policySets = new List<PolicySet>
        {
            new()
            {
                Id = "ps-1",
                Name = "Test Policy Set",
                IsActive = true,
                Policies =
                [
                    new Policy
                    {
                        Id = "p-1",
                        PolicySetId = "ps-1",
                        Name = "Deny SECRET",
                        Versions =
                        [
                            new PolicyVersion
                            {
                                PolicyId = "p-1",
                                VersionNumber = 1,
                                Content = policyContent,
                                Hash = "test-hash",
                                IsActive = true
                            }
                        ]
                    }
                ]
            }
        };

        var engine = BuildEngine(spifIndex, policySets: policySets);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Single(result.Classifications);
        Assert.Equal("CONFIDENTIAL", result.Classifications[0].Name);
    }

    [Fact]
    public async Task Evaluate_AllowsAll_WhenPolicyReturnsNotApplicable()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = MakeClearance("1.2.3.4", 2, 3);

        // Policy that only matches action=delete (not classify)
        var policyContent = """
{
  "rules": [
    {
      "id": "deny-delete",
      "effect": "Deny",
      "conditions": [
        { "path": "action.name", "equals": "delete" }
      ]
    }
  ]
}
""";
        var policySets = new List<PolicySet>
        {
            new()
            {
                Id = "ps-1",
                Name = "Test Policy Set",
                IsActive = true,
                Policies =
                [
                    new Policy
                    {
                        Id = "p-1",
                        PolicySetId = "ps-1",
                        Name = "Unrelated Policy",
                        Versions =
                        [
                            new PolicyVersion
                            {
                                PolicyId = "p-1",
                                VersionNumber = 1,
                                Content = policyContent,
                                Hash = "test-hash",
                                IsActive = true
                            }
                        ]
                    }
                ]
            }
        };

        var engine = BuildEngine(spifIndex, policySets: policySets);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Equal(2, result.Classifications.Count);
    }

    // ── Application Scope Filter Tests ──

    [Fact]
    public async Task Evaluate_FiltersBy_ApplicationLacvWhitelist()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = MakeClearance("1.2.3.4", 2, 3);
        var apps = new List<ApplicationRegistration>
        {
            new()
            {
                Id = "email-app",
                Name = "Email Classification",
                AllowedClassificationLacvs = [2], // Only CONFIDENTIAL
                IsActive = true
            }
        };

        var engine = BuildEngine(spifIndex, apps: apps);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            ApplicationId = "email-app",
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Single(result.Classifications);
        Assert.Equal("CONFIDENTIAL", result.Classifications[0].Name);
    }

    [Fact]
    public async Task Evaluate_FiltersBy_ApplicationMaxHierarchy()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = MakeClearance("1.2.3.4", 2, 3);
        var apps = new List<ApplicationRegistration>
        {
            new()
            {
                Id = "email-app",
                Name = "Email Classification",
                MaxClassificationHierarchy = 2, // Only up to CONFIDENTIAL
                IsActive = true
            }
        };

        var engine = BuildEngine(spifIndex, apps: apps);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            ApplicationId = "email-app",
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Single(result.Classifications);
        Assert.Equal("CONFIDENTIAL", result.Classifications[0].Name);
    }

    [Fact]
    public async Task Evaluate_EmptyWhitelist_MeansAllAllowed()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = MakeClearance("1.2.3.4", 2, 3);
        var apps = new List<ApplicationRegistration>
        {
            new()
            {
                Id = "email-app",
                Name = "Email Classification",
                AllowedClassificationLacvs = [], // Empty = all allowed
                IsActive = true
            }
        };

        var engine = BuildEngine(spifIndex, apps: apps);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            ApplicationId = "email-app",
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Equal(2, result.Classifications.Count);
    }

    [Fact]
    public async Task Evaluate_InactiveApp_SkipsApplicationFilter()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = MakeClearance("1.2.3.4", 2, 3);
        var apps = new List<ApplicationRegistration>
        {
            new()
            {
                Id = "email-app",
                Name = "Email Classification",
                AllowedClassificationLacvs = [2], // Only CONFIDENTIAL
                IsActive = false // Inactive
            }
        };

        var engine = BuildEngine(spifIndex, apps: apps);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            ApplicationId = "email-app",
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        // Inactive app = no application filter applied
        Assert.Equal(2, result.Classifications.Count);
    }

    // ── Trace Tests ──

    [Fact]
    public async Task Evaluate_IncludesTrace_WhenRequested()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = MakeClearance("1.2.3.4", 2); // Only CONFIDENTIAL
        var engine = BuildEngine(spifIndex);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            IncludeTrace = true,
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.NotNull(result.Trace);
        Assert.NotEmpty(result.Trace!.Steps);

        // CONFIDENTIAL should pass clearance
        var confClearance = result.Trace.Steps
            .Single(s => s.ClassificationName == "CONFIDENTIAL" && s.FilterLayer == "clearance_filter");
        Assert.True(confClearance.Passed);

        // SECRET should fail clearance
        var secretClearance = result.Trace.Steps
            .Single(s => s.ClassificationName == "SECRET" && s.FilterLayer == "clearance_filter");
        Assert.False(secretClearance.Passed);
    }

    [Fact]
    public async Task Evaluate_OmitsTrace_WhenNotRequested()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = MakeClearance("1.2.3.4", 2, 3);
        var engine = BuildEngine(spifIndex);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            IncludeTrace = false,
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Null(result.Trace);
    }

    // ── Result Metadata Tests ──

    [Fact]
    public async Task Evaluate_PopulatesResultMetadata()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = MakeClearance("1.2.3.4", 2, 3);
        var engine = BuildEngine(spifIndex);

        var query = new ClassificationAssignmentQuery
        {
            RequestId = "req-1",
            Subject = MakeSubject(clearance),
            ApplicationId = "my-app",
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Equal("req-1", result.RequestId);
        Assert.Equal("1.2.3.4", result.PolicyOid);
        Assert.Equal("TEST", result.PolicyName);
        Assert.Equal("my-app", result.ApplicationId);
        Assert.Equal(2, result.TotalSpifClassifications);
        Assert.NotNull(result.ResultId);
        Assert.True(result.EvaluationTime > TimeSpan.Zero);
    }

    [Fact]
    public async Task Evaluate_IncludesMarkingData_WhenRequested()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = MakeClearance("1.2.3.4", 2, 3);
        var engine = BuildEngine(spifIndex);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            IncludeMarkingData = true,
            IncludeCategories = false
        };

        var result = await engine.EvaluateAsync(query);

        Assert.Equal(2, result.Classifications.Count);
        // MarkingData should be present (even if empty list) when requested
        foreach (var c in result.Classifications)
        {
            Assert.NotNull(c.MarkingData);
        }
    }

    // ── Batch Tests ──

    [Fact]
    public async Task EvaluateBatch_ReturnsOneResultPerQuery()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = MakeClearance("1.2.3.4", 2, 3);
        var engine = BuildEngine(spifIndex);

        var queries = new List<ClassificationAssignmentQuery>
        {
            new()
            {
                Subject = MakeSubject(clearance),
                IncludeCategories = false
            },
            new()
            {
                Subject = MakeSubject(MakeClearance("1.2.3.4", 2)),
                IncludeCategories = false
            }
        };

        var results = await engine.EvaluateBatchAsync(queries);

        Assert.Equal(2, results.Count);
        Assert.Equal(2, results[0].Classifications.Count);
        Assert.Single(results[1].Classifications);
    }

    // ── Category Filtering Tests ──

    [Fact]
    public async Task Evaluate_IncludesAllowedCategories_WhenClearanceHasThem()
    {
        var spifIndex = BuildSpifIndex();
        var clearance = new SecurityClearance
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacvs = ImmutableHashSet.Create<LacvValue>((LacvValue)2, (LacvValue)3),
            CategoryTagSets = ImmutableList.Create(
                new ClearanceCategoryTagSet
                {
                    TagSetOid = "1.2.3.4.1",
                    Tags = ImmutableList.Create(
                        new ClearanceCategoryTag
                        {
                            TagType = TagType.Restrictive,
                            Bits = ImmutableHashSet.Create<LacvValue>((LacvValue)10) // ALPHA only
                        })
                })
        };

        var engine = BuildEngine(spifIndex);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            IncludeCategories = true
        };

        var result = await engine.EvaluateAsync(query);

        var secret = result.Classifications.First(c => c.Name == "SECRET");
        Assert.NotNull(secret.AllowedCategories);
        Assert.NotEmpty(secret.AllowedCategories!);

        // Should contain SCI tag with only ALPHA (not BRAVO)
        var sciTag = secret.AllowedCategories!
            .SelectMany(ts => ts.Tags)
            .FirstOrDefault(t => t.Name == "SCI");
        Assert.NotNull(sciTag);
        Assert.Single(sciTag!.Categories);
        Assert.Equal("ALPHA", sciTag.Categories[0].Name);
    }

    // ── Three-Layer Composition Test ──

    [Fact]
    public async Task Evaluate_ThreeLayerComposition_IntersectsCorrectly()
    {
        const string xml = """
<spif:SPIF xmlns:spif="http://www.xmlspif.org/spif" schemaVersion="2.1" creationDate="20260101000000Z">
  <spif:securityPolicyId name="TEST" id="1.2.3.4" />
  <spif:securityClassifications>
    <spif:securityClassification name="UNCLASSIFIED" lacv="1" hierarchy="1" />
    <spif:securityClassification name="CONFIDENTIAL" lacv="2" hierarchy="2" />
    <spif:securityClassification name="SECRET" lacv="3" hierarchy="3" />
    <spif:securityClassification name="TOP SECRET" lacv="4" hierarchy="4" />
  </spif:securityClassifications>
</spif:SPIF>
""";
        var spifIndex = BuildSpifIndex(xml);

        // Clearance allows up to SECRET (hierarchy 3)
        var clearance = MakeClearance("1.2.3.4", 1, 2, 3);

        // Policy denies UNCLASSIFIED for action=classify
        var policyContent = """
{
  "rules": [
    {
      "id": "deny-unclass",
      "effect": "Deny",
      "conditions": [
        { "path": "resource.properties.classificationName", "equals": "UNCLASSIFIED" },
        { "path": "action.name", "equals": "classify" }
      ]
    }
  ]
}
""";
        var policySets = new List<PolicySet>
        {
            new()
            {
                Id = "ps-1", Name = "PS1", IsActive = true,
                Policies =
                [
                    new Policy
                    {
                        Id = "p-1", PolicySetId = "ps-1", Name = "P1",
                        Versions =
                        [
                            new PolicyVersion
                            {
                                PolicyId = "p-1", VersionNumber = 1,
                                Content = policyContent, Hash = "h1", IsActive = true
                            }
                        ]
                    }
                ]
            }
        };

        // App only allows CONFIDENTIAL and SECRET
        var apps = new List<ApplicationRegistration>
        {
            new()
            {
                Id = "email",
                Name = "Email",
                AllowedClassificationLacvs = [2, 3],
                IsActive = true
            }
        };

        var engine = BuildEngine(spifIndex, apps: apps, policySets: policySets);

        var query = new ClassificationAssignmentQuery
        {
            Subject = MakeSubject(clearance),
            ApplicationId = "email",
            IncludeCategories = false,
            IncludeTrace = true
        };

        var result = await engine.EvaluateAsync(query);

        // After three-layer filtering:
        // UNCLASSIFIED: passes clearance, denied by policy → filtered
        // CONFIDENTIAL: passes clearance, passes policy (not applicable), passes app → allowed
        // SECRET: passes clearance, passes policy (not applicable), passes app → allowed
        // TOP SECRET: fails clearance → filtered
        Assert.Equal(2, result.Classifications.Count);
        Assert.Equal("CONFIDENTIAL", result.Classifications[0].Name);
        Assert.Equal("SECRET", result.Classifications[1].Name);
    }

    // ── Stubs ──

    private sealed class StubSpifRegistry : ISpifRegistry
    {
        private readonly ISpifIndex? _index;
        public StubSpifRegistry(ISpifIndex? index) => _index = index;
        public ISpifIndex? GetByPolicyOid(string policyOid) => _index?.PolicyOid == policyOid ? _index : null;
        public ISpifIndex? GetDefault() => _index;
        public void Register(ISpifIndex spifIndex) { }
        public void SetDefault(string policyOid) { }
        public void Remove(string policyOid) { }
        public bool IsRegistered(string policyOid) => _index?.PolicyOid == policyOid;
        public IReadOnlyList<string> GetRegisteredPolicyOids() => _index is not null ? [_index.PolicyOid] : [];
    }

    private sealed class StubPolicyRepository : IPolicyRepository
    {
        private readonly List<PolicySet> _policySets;
        public StubPolicyRepository(List<PolicySet> policySets) => _policySets = policySets;
        public Task<List<PolicySet>> GetPolicySetsAsync(CancellationToken ct = default) => Task.FromResult(_policySets);
        public Task<PolicySet?> GetPolicySetAsync(string id, CancellationToken ct = default) => Task.FromResult(_policySets.FirstOrDefault(ps => ps.Id == id));
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
        private readonly List<ApplicationRegistration> _apps;
        public StubApplicationRepository(List<ApplicationRegistration> apps) => _apps = apps;
        public Task<List<ApplicationRegistration>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(_apps);
        public Task<ApplicationRegistration?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult(_apps.FirstOrDefault(a => a.Id == id));
        public Task<ApplicationRegistration> UpsertAsync(ApplicationRegistration registration, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class StubAuditWriter : IAuditWriter
    {
        public readonly List<AuditEvent> Events = [];
        public void Write(AuditEvent auditEvent) => Events.Add(auditEvent);
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(string? tenantId) => TenantId = tenantId;
        public string? TenantId { get; }
    }
}
