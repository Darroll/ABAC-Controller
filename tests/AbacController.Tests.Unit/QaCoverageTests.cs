using System.Collections.Immutable;
using AbacController.Audit;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Attributes;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Policy;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;
using AbacController.Pap;
using AbacController.Pdp;
using AbacController.Pep.Codecs;
using AbacController.Pip;
using Microsoft.Extensions.Caching.Memory;

namespace AbacController.Tests.Unit;

public sealed class QaCoverageTests
{
    [Fact]
    public void Parse_Succeeds_For_V21_With_Known_Namespace_Typo()
    {
        const string xml = """
<x:SPIF xmlns:x="http://www.xmslpif.org/spif" schemaVersion="2.1">
  <x:securityPolicyId name="TEST" id="1.2.3.4" />
  <x:securityClassifications>
    <x:securityClassification name="SECRET" lacv="3" hierarchy="3" />
  </x:securityClassifications>
</x:SPIF>
""";

        var result = new SpifParser().Parse(xml);

        Assert.True(result.Success, string.Join(" | ", result.Errors.Select(e => e.Message)));
        Assert.NotNull(result.Spif);
        Assert.Equal("2.1", result.Spif!.SchemaVersion);
        Assert.Equal("1.2.3.4", result.Spif.PolicyId.Oid);
    }

    [Fact]
    public async Task EvaluateAsync_Returns_NotApplicable_When_No_Policy_Matches()
    {
        var spif = BuildSpif();
        var engine = new PdpEngine(
            new AcdfEvaluator(),
            new StubSpifRegistry(spif),
            new DecisionCache(new MemoryCache(new MemoryCacheOptions())),
            new StubPolicyRepository(new PolicySet
            {
                Id = "ops",
                Name = "Operations",
                CombiningAlgorithm = "deny-overrides",
                Policies =
                [
                    BuildPolicy("permit-finance", "{" +
                        "\"effect\":\"Permit\"," +
                        "\"conditions\":[{" +
                        "\"path\":\"subject.department\"," +
                        "\"equals\":\"FIN\"}]}")
                ]
            }),
            new StubPipResolver(AttributeResolutionResult.Succeeded([])),
            new CapturingAuditWriter());

        var result = await engine.EvaluateAsync(BuildRequest(new Dictionary<string, object?>
        {
            ["department"] = "ENG",
            ["securityClearance"] = BuildClearance()
        }));

        Assert.Equal(Decision.NotApplicable, result.Decision);
        Assert.Equal("No applicable policy matched", result.Status?.Message);
    }

    [Fact]
    public async Task EvaluateAsync_Returns_Indeterminate_When_Clearance_Cannot_Be_Resolved()
    {
        var spif = BuildSpif();
        var engine = new PdpEngine(
            new AcdfEvaluator(),
            new StubSpifRegistry(spif),
            new DecisionCache(new MemoryCache(new MemoryCacheOptions())),
            new StubPolicyRepository(),
            new StubPipResolver(new AttributeResolutionResult
            {
                Success = false,
                Values = [],
                Missing = ["securityClearance"]
            }),
            new CapturingAuditWriter());

        var result = await engine.EvaluateAsync(BuildRequest(new Dictionary<string, object?>()));

        Assert.Equal(Decision.Indeterminate, result.Decision);
        Assert.Contains("Missing required attributes", result.Status?.Message);
    }

    [Fact]
    public async Task EvaluateAsync_Writes_Audit_Event_With_Core_Decision_Details()
    {
        var spif = BuildSpif();
        var audit = new CapturingAuditWriter();
        var engine = new PdpEngine(
            new AcdfEvaluator(),
            new StubSpifRegistry(spif),
            new DecisionCache(new MemoryCache(new MemoryCacheOptions())),
            new StubPolicyRepository(new PolicySet
            {
                Id = "ops",
                Name = "Operations",
                CombiningAlgorithm = "permit-overrides",
                Policies =
                [
                    BuildPolicy("permit-eng", "{" +
                        "\"rules\":[{" +
                        "\"id\":\"permit-eng\"," +
                        "\"effect\":\"Permit\"," +
                        "\"conditions\":[{" +
                        "\"path\":\"subject.department\"," +
                        "\"equals\":\"ENG\"}]}]}")
                ]
            }),
            new StubPipResolver(new AttributeResolutionResult
            {
                Success = true,
                Values =
                [
                    new AttributeValue
                    {
                        Name = "department",
                        Category = AttributeCategory.Subject,
                        Value = "ENG",
                        SourceId = "pip-static",
                        SourceType = "static",
                        FetchedAt = DateTimeOffset.UtcNow,
                        CacheTtl = TimeSpan.FromMinutes(1)
                    },
                    new AttributeValue
                    {
                        Name = "securityClearance",
                        Category = AttributeCategory.Subject,
                        Value = BuildClearance(),
                        SourceId = "pip-static",
                        SourceType = "static",
                        FetchedAt = DateTimeOffset.UtcNow,
                        CacheTtl = TimeSpan.FromMinutes(1)
                    }
                ],
                Missing = []
            }),
            audit);

        var result = await engine.EvaluateAsync(BuildRequest(new Dictionary<string, object?>()));

        Assert.Equal(Decision.Permit, result.Decision);
        var evt = Assert.Single(audit.Events);
        Assert.Equal("evaluation", evt.EventType);
        Assert.Equal(result.DecisionId, evt.DecisionId);
        Assert.Equal("alice", evt.SubjectId);
        Assert.Equal("write", evt.ActionName);
        Assert.Equal("doc-1", evt.ResourceId);
        Assert.Equal("Permit", evt.Decision);
        Assert.NotNull(evt.AppliedPolicies);
        Assert.Contains("permit-eng", evt.AppliedPolicies);
        Assert.NotNull(evt.AttributesUsedJson);
        Assert.Contains("securityClearance", evt.AttributesUsedJson);
        Assert.True(evt.EvaluationTimeMs > 0);
    }

    [Fact]
    public async Task PipResolver_Uses_Source_Priority_And_Caches_Resolved_Attributes()
    {
        var cache = new PipCacheManager();
        var source1 = new CountingPipSource(
            sourceId: "fast",
            priority: 1,
            providesAttributes: new HashSet<string>(StringComparer.Ordinal) { "department" },
            values: new Dictionary<string, object?> { ["department"] = "ENG" });
        var source2 = new CountingPipSource(
            sourceId: "slow",
            priority: 2,
            providesAttributes: new HashSet<string>(StringComparer.Ordinal) { "department" },
            values: new Dictionary<string, object?> { ["department"] = "FIN" });
        var resolver = new PipResolver([source2, source1], cache);

        var request = new AttributeResolutionRequest
        {
            SubjectId = "alice",
            SubjectType = "user",
            RequestedAttributes = ["department"]
        };

        var first = await resolver.ResolveAsync(request);
        var second = await resolver.ResolveAsync(request);

        Assert.True(first.Success);
        Assert.Equal("ENG", first.Values.Single().Value);
        Assert.Equal(1, source1.ResolveCount);
        Assert.Equal(0, source2.ResolveCount);

        Assert.True(second.Success);
        Assert.True(second.Values.Single().FromCache);
        Assert.Equal(1, source1.ResolveCount);
        Assert.Equal(0, source2.ResolveCount);
    }

    [Fact]
    public void LabelCodecRegistry_Supports_Pluggable_Codec_Registration_And_Lookup()
    {
        var registry = new LabelCodecRegistry();
        var codec = new TestCodec();

        registry.Register(codec);

        Assert.Same(codec, registry.GetCodec("test-codec"));
        Assert.Same(codec, registry.GetCodecByContentType("application/test"));
        Assert.Contains("test-codec", registry.GetRegisteredCodecIds());
    }

    private static EvaluationRequest BuildRequest(Dictionary<string, object?> subjectProperties)
        => new()
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Subject = new SubjectInfo
            {
                Type = "user",
                Id = "alice",
                Properties = subjectProperties
            },
            Action = new ActionInfo { Name = "write" },
            Resource = new ResourceInfo
            {
                Type = "document",
                Id = "doc-1",
                Properties = new Dictionary<string, object?>
                {
                    ["securityLabel"] = BuildLabel()
                }
            },
            Context = new ContextInfo(),
            Options = new EvaluateOptions { PolicySetId = "ops" }
        };

    private static SecurityLabel BuildLabel()
        => new()
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacv = 1,
            CategoryTagSets = []
        };

    private static SecurityClearance BuildClearance()
        => new()
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacvs = [1, 2],
            CategoryTagSets = []
        };

    private static SpifIndex BuildSpif()
        => new(new Spif
        {
            SchemaVersion = "3.0",
            Version = "1",
            PolicyId = new PolicyInfo { Name = "Test SPIF", Oid = "1.2.3.4" },
            Classifications =
            [
                new SecurityClassification { Name = "SECRET", Lacv = 1, Hierarchy = 10 },
                new SecurityClassification { Name = "TOP SECRET", Lacv = 2, Hierarchy = 20 }
            ],
            CategoryTagSets = []
        });

    private static Policy BuildPolicy(string id, string content)
        => new()
        {
            Id = id,
            PolicySetId = "ops",
            Name = id,
            Versions =
            [
                new PolicyVersion
                {
                    PolicyId = id,
                    VersionNumber = 1,
                    Content = content,
                    Hash = "hash",
                    IsActive = true
                }
            ]
        };

    private sealed class StubSpifRegistry(SpifIndex spif) : ISpifRegistry
    {
        public ISpifIndex? GetByPolicyOid(string policyOid)
            => string.Equals(policyOid, spif.PolicyOid, StringComparison.Ordinal) ? spif : null;

        public ISpifIndex? GetDefault() => spif;
        public void Register(ISpifIndex spifIndex) { }
        public void SetDefault(string policyOid) { }
        public void Remove(string policyOid) { }
        public IReadOnlyList<string> GetRegisteredPolicyOids() => [spif.PolicyOid];
        public bool IsRegistered(string policyOid) => string.Equals(policyOid, spif.PolicyOid, StringComparison.Ordinal);
    }

    private sealed class StubPolicyRepository(params PolicySet[] policySets) : IPolicyRepository
    {
        private readonly List<PolicySet> _policySets = policySets.ToList();

        public Task<List<PolicySet>> GetPolicySetsAsync(CancellationToken ct = default)
            => Task.FromResult(_policySets.ToList());

        public Task<PolicySet?> GetPolicySetAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_policySets.FirstOrDefault(ps => ps.Id == id));

        public Task<PolicySet> CreatePolicySetAsync(PolicySet policySet, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PolicySet> UpdatePolicySetAsync(PolicySet policySet, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeletePolicySetAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Policy?> GetPolicyAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Policy> CreatePolicyAsync(Policy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Policy> UpdatePolicyAsync(Policy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeletePolicyAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<PolicyVersion>> GetVersionsAsync(string policyId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PolicyVersion?> GetVersionAsync(Guid versionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PolicyVersion> CreateVersionAsync(PolicyVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ActivateVersionAsync(string policyId, Guid versionId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubPipResolver(AttributeResolutionResult result) : IPipResolver
    {
        public Task<AttributeResolutionResult> ResolveAsync(AttributeResolutionRequest request, CancellationToken ct = default)
            => Task.FromResult(result);
    }

    private sealed class CapturingAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = [];

        public void Write(AuditEvent auditEvent) => Events.Add(auditEvent);
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CountingPipSource : IPipSource
    {
        private readonly Dictionary<string, object?> _values;

        public CountingPipSource(string sourceId, int priority, IReadOnlySet<string> providesAttributes, Dictionary<string, object?> values)
        {
            SourceId = sourceId;
            Priority = priority;
            ProvidesAttributes = providesAttributes;
            _values = values;
        }

        public string SourceType => "test";
        public string SourceId { get; }
        public IReadOnlySet<string> ProvidesAttributes { get; }
        public int Priority { get; }
        public int ResolveCount { get; private set; }

        public Task<AttributeResolutionResult> ResolveAsync(AttributeResolutionRequest request, CancellationToken ct = default)
        {
            ResolveCount++;
            var values = request.RequestedAttributes
                .Where(_values.ContainsKey)
                .Select(name => new AttributeValue
                {
                    Name = name,
                    Category = AttributeCategory.Subject,
                    Value = _values[name]!,
                    SourceId = SourceId,
                    SourceType = SourceType,
                    FetchedAt = DateTimeOffset.UtcNow,
                    CacheTtl = TimeSpan.FromMinutes(5)
                })
                .ToList();

            return Task.FromResult(AttributeResolutionResult.Succeeded(values));
        }

        public Task<SourceHealthResult> TestConnectivityAsync(CancellationToken ct = default)
            => Task.FromResult(new SourceHealthResult { Healthy = true, Message = "OK" });
    }

    private sealed class TestCodec : ILabelCodec
    {
        public string CodecId => "test-codec";
        public string ContentType => "application/test";

        public EncodeResult Encode(SecurityLabel label, ISpifIndex spifIndex) => EncodeResult.Success("encoded");
        public DecodeResult Decode(ReadOnlySpan<byte> encodedLabel) => DecodeResult.Success(new SecurityLabel());
        public DecodeResult Decode(string encodedLabel) => DecodeResult.Success(new SecurityLabel());
    }
}
