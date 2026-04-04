using System.Collections.Immutable;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Attributes;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Policy;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;
using AbacController.Pdp;
using Microsoft.Extensions.Caching.Memory;

namespace AbacController.Tests.Unit;

public sealed class PdpReworkTests
{
    [Fact]
    public async Task EvaluateAsync_UsesPipResolvedClearanceAndPolicyRules()
    {
        var spif = BuildSpif();
        var resolver = new StubPipResolver(new AttributeResolutionResult
        {
            Success = true,
            Values =
            [
                new AttributeValue
                {
                    Name = "securityClearance",
                    Category = AttributeCategory.Subject,
                    Value = BuildClearance(),
                    SourceId = "pip-static",
                    SourceType = "static",
                    FetchedAt = DateTimeOffset.UtcNow,
                    CacheTtl = TimeSpan.FromSeconds(30)
                },
                new AttributeValue
                {
                    Name = "department",
                    Category = AttributeCategory.Subject,
                    Value = "ENG",
                    SourceId = "pip-static",
                    SourceType = "static",
                    FetchedAt = DateTimeOffset.UtcNow,
                    CacheTtl = TimeSpan.FromSeconds(30)
                }
            ],
            Missing = []
        });
        var cache = new CapturingDecisionCache();
        var engine = new PdpEngine(
            new AcdfEvaluator(),
            new StubSpifRegistry(spif),
            cache,
            new StubPolicyRepository(
                BuildPolicySet(
                    "ops",
                    "permit-overrides",
                    "permit-readers",
                    "{" +
                    "\"rules\":[{" +
                    "\"id\":\"permit-eng\"," +
                    "\"effect\":\"Permit\"," +
                    "\"conditions\":[{" +
                    "\"path\":\"subject.department\"," +
                    "\"equals\":\"ENG\"}]," +
                    "\"obligations\":[{" +
                    "\"id\":\"log-access\"}]," +
                    "\"advice\":[{" +
                    "\"id\":\"notify-owner\"}]}]}")),
            resolver,
            new NullAuditWriter());

        var request = BuildRequest(includeClearance: false, subjectProperties: new Dictionary<string, object?>());
        var result = await engine.EvaluateAsync(request with
        {
            Options = new EvaluateOptions { PolicySetId = "ops", ReturnObligations = true, ReturnAdvice = true }
        });

        Assert.Equal(Decision.Permit, result.Decision);
        Assert.Contains(result.AppliedPolicies, static p => p == "policy-set:ops");
        Assert.Contains(result.AppliedPolicies, static p => p.StartsWith("policy:permit-readers@v1", StringComparison.Ordinal));
        Assert.Single(result.Obligations);
        Assert.Single(result.Advice);
        Assert.Equal(2, result.AttributeProvenance.Count);
        Assert.Equal(TimeSpan.FromSeconds(30), cache.LastTtl);
        Assert.Equal(new[] { "department", "securityClearance" }, resolver.LastRequest!.RequestedAttributes.OrderBy(static x => x).ToArray());
    }

    [Fact]
    public async Task EvaluateAsync_CombinesPoliciesUsingDenyOverrides()
    {
        var spif = BuildSpif();
        var engine = new PdpEngine(
            new AcdfEvaluator(),
            new StubSpifRegistry(spif),
            new CapturingDecisionCache(),
            new StubPolicyRepository(
                new PolicySet
                {
                    Id = "ops",
                    Name = "Operations",
                    CombiningAlgorithm = "deny-overrides",
                    Policies =
                    [
                        BuildPolicy("permit-readers", "{" +
                            "\"effect\":\"Permit\"," +
                            "\"conditions\":[{" +
                            "\"path\":\"subject.department\"," +
                            "\"equals\":\"ENG\"}]}"),
                        BuildPolicy("deny-writes", "{" +
                            "\"effect\":\"Deny\"," +
                            "\"conditions\":[{" +
                            "\"path\":\"action.name\"," +
                            "\"equals\":\"write\"}]}"),
                    ]
                }),
            new StubPipResolver(AttributeResolutionResult.Succeeded([])),
            new NullAuditWriter());

        var result = await engine.EvaluateAsync(BuildRequest(subjectProperties: new Dictionary<string, object?>
        {
            ["department"] = "ENG",
            ["securityClearance"] = BuildClearance()
        }));

        Assert.Equal(Decision.Deny, result.Decision);
        Assert.Contains(result.AppliedPolicies, static p => p == "policy-set:ops");
    }

    [Fact]
    public async Task EvaluateAsync_CombinesPoliciesUsingPermitOverrides()
    {
        var spif = BuildSpif();
        var engine = new PdpEngine(
            new AcdfEvaluator(),
            new StubSpifRegistry(spif),
            new CapturingDecisionCache(),
            new StubPolicyRepository(
                new PolicySet
                {
                    Id = "ops",
                    Name = "Operations",
                    CombiningAlgorithm = "permit-overrides",
                    Policies =
                    [
                        BuildPolicy("permit-readers", "{" +
                            "\"effect\":\"Permit\"," +
                            "\"conditions\":[{" +
                            "\"path\":\"subject.department\"," +
                            "\"equals\":\"ENG\"}]}"),
                        BuildPolicy("deny-writes", "{" +
                            "\"effect\":\"Deny\"," +
                            "\"conditions\":[{" +
                            "\"path\":\"action.name\"," +
                            "\"equals\":\"write\"}]}"),
                    ]
                }),
            new StubPipResolver(AttributeResolutionResult.Succeeded([])),
            new NullAuditWriter());

        var result = await engine.EvaluateAsync(BuildRequest(subjectProperties: new Dictionary<string, object?>
        {
            ["department"] = "ENG",
            ["securityClearance"] = BuildClearance()
        }));

        Assert.Equal(Decision.Permit, result.Decision);
        Assert.Contains(result.AppliedPolicies, static p => p == "policy-set:ops");
    }

    [Fact]
    public async Task EvaluateAsync_CombinesPoliciesUsingFirstApplicable()
    {
        var spif = BuildSpif();
        var engine = new PdpEngine(
            new AcdfEvaluator(),
            new StubSpifRegistry(spif),
            new CapturingDecisionCache(),
            new StubPolicyRepository(
                new PolicySet
                {
                    Id = "ops",
                    Name = "Operations",
                    CombiningAlgorithm = "first-applicable",
                    Policies =
                    [
                        BuildPolicy("deny-writes", "{" +
                            "\"effect\":\"Deny\"," +
                            "\"conditions\":[{" +
                            "\"path\":\"action.name\"," +
                            "\"equals\":\"write\"}]}"),
                        BuildPolicy("permit-readers", "{" +
                            "\"effect\":\"Permit\"," +
                            "\"conditions\":[{" +
                            "\"path\":\"subject.department\"," +
                            "\"equals\":\"ENG\"}]}"),
                    ]
                }),
            new StubPipResolver(AttributeResolutionResult.Succeeded([])),
            new NullAuditWriter());

        var result = await engine.EvaluateAsync(BuildRequest(subjectProperties: new Dictionary<string, object?>
        {
            ["department"] = "ENG",
            ["securityClearance"] = BuildClearance()
        }));

        // First applicable is the deny-writes rule (action=write matches)
        Assert.Equal(Decision.Deny, result.Decision);
    }

    [Fact]
    public async Task EvaluateAsync_CombinesPoliciesUsingOnlyOneApplicable_SingleMatch()
    {
        var spif = BuildSpif();
        var engine = new PdpEngine(
            new AcdfEvaluator(),
            new StubSpifRegistry(spif),
            new CapturingDecisionCache(),
            new StubPolicyRepository(
                new PolicySet
                {
                    Id = "ops",
                    Name = "Operations",
                    CombiningAlgorithm = "only-one-applicable",
                    Policies =
                    [
                        BuildPolicy("permit-readers", "{" +
                            "\"effect\":\"Permit\"," +
                            "\"conditions\":[{" +
                            "\"path\":\"subject.department\"," +
                            "\"equals\":\"ENG\"}]}"),
                        BuildPolicy("deny-sales", "{" +
                            "\"effect\":\"Deny\"," +
                            "\"conditions\":[{" +
                            "\"path\":\"subject.department\"," +
                            "\"equals\":\"SALES\"}]}"),
                    ]
                }),
            new StubPipResolver(AttributeResolutionResult.Succeeded([])),
            new NullAuditWriter());

        var result = await engine.EvaluateAsync(BuildRequest(subjectProperties: new Dictionary<string, object?>
        {
            ["department"] = "ENG",
            ["securityClearance"] = BuildClearance()
        }));

        Assert.Equal(Decision.Permit, result.Decision);
    }

    [Fact]
    public async Task EvaluateAsync_CombinesPoliciesUsingOnlyOneApplicable_MultipleMatch_ReturnsIndeterminate()
    {
        var spif = BuildSpif();
        var engine = new PdpEngine(
            new AcdfEvaluator(),
            new StubSpifRegistry(spif),
            new CapturingDecisionCache(),
            new StubPolicyRepository(
                new PolicySet
                {
                    Id = "ops",
                    Name = "Operations",
                    CombiningAlgorithm = "only-one-applicable",
                    Policies =
                    [
                        BuildPolicy("permit-readers", "{" +
                            "\"effect\":\"Permit\"," +
                            "\"conditions\":[{" +
                            "\"path\":\"subject.department\"," +
                            "\"equals\":\"ENG\"}]}"),
                        BuildPolicy("deny-writes", "{" +
                            "\"effect\":\"Deny\"," +
                            "\"conditions\":[{" +
                            "\"path\":\"action.name\"," +
                            "\"equals\":\"write\"}]}"),
                    ]
                }),
            new StubPipResolver(AttributeResolutionResult.Succeeded([])),
            new NullAuditWriter());

        var result = await engine.EvaluateAsync(BuildRequest(subjectProperties: new Dictionary<string, object?>
        {
            ["department"] = "ENG",
            ["securityClearance"] = BuildClearance()
        }));

        // Both rules match, so only-one-applicable returns Indeterminate
        Assert.Equal(Decision.Indeterminate, result.Decision);
    }

    [Fact]
    public async Task EvaluateAsync_CombinesPoliciesUsingDenyUnlessPermit()
    {
        var spif = BuildSpif();
        var engine = new PdpEngine(
            new AcdfEvaluator(),
            new StubSpifRegistry(spif),
            new CapturingDecisionCache(),
            new StubPolicyRepository(
                new PolicySet
                {
                    Id = "ops",
                    Name = "Operations",
                    CombiningAlgorithm = "deny-unless-permit",
                    Policies =
                    [
                        BuildPolicy("permit-readers", "{" +
                            "\"effect\":\"Permit\"," +
                            "\"conditions\":[{" +
                            "\"path\":\"subject.department\"," +
                            "\"equals\":\"ENG\"}]}"),
                    ]
                }),
            new StubPipResolver(AttributeResolutionResult.Succeeded([])),
            new NullAuditWriter());

        // Matches permit rule
        var result = await engine.EvaluateAsync(BuildRequest(subjectProperties: new Dictionary<string, object?>
        {
            ["department"] = "ENG",
            ["securityClearance"] = BuildClearance()
        }) with { Options = new EvaluateOptions { BypassCache = true } });
        Assert.Equal(Decision.Permit, result.Decision);

        // Does not match permit rule → deny (not NotApplicable)
        var result2 = await engine.EvaluateAsync(BuildRequest(subjectProperties: new Dictionary<string, object?>
        {
            ["department"] = "SALES",
            ["securityClearance"] = BuildClearance()
        }) with { Options = new EvaluateOptions { BypassCache = true } });
        Assert.Equal(Decision.Deny, result2.Decision);
    }

    [Fact]
    public async Task EvaluateAsync_CombinesPoliciesUsingPermitUnlessDeny()
    {
        var spif = BuildSpif();
        var engine = new PdpEngine(
            new AcdfEvaluator(),
            new StubSpifRegistry(spif),
            new CapturingDecisionCache(),
            new StubPolicyRepository(
                new PolicySet
                {
                    Id = "ops",
                    Name = "Operations",
                    CombiningAlgorithm = "permit-unless-deny",
                    Policies =
                    [
                        BuildPolicy("deny-sales", "{" +
                            "\"effect\":\"Deny\"," +
                            "\"conditions\":[{" +
                            "\"path\":\"subject.department\"," +
                            "\"equals\":\"SALES\"}]}"),
                    ]
                }),
            new StubPipResolver(AttributeResolutionResult.Succeeded([])),
            new NullAuditWriter());

        // Does not match deny rule → permit (not NotApplicable)
        var result = await engine.EvaluateAsync(BuildRequest(subjectProperties: new Dictionary<string, object?>
        {
            ["department"] = "ENG",
            ["securityClearance"] = BuildClearance()
        }) with { Options = new EvaluateOptions { BypassCache = true } });
        Assert.Equal(Decision.Permit, result.Decision);

        // Matches deny rule
        var result2 = await engine.EvaluateAsync(BuildRequest(subjectProperties: new Dictionary<string, object?>
        {
            ["department"] = "SALES",
            ["securityClearance"] = BuildClearance()
        }) with { Options = new EvaluateOptions { BypassCache = true } });
        Assert.Equal(Decision.Deny, result2.Decision);
    }

    [Fact]
    public async Task EvaluateExplainAsync_ReturnsDetailedTrace()
    {
        var spif = BuildSpif();
        var engine = new PdpEngine(
            new AcdfEvaluator(),
            new StubSpifRegistry(spif),
            new CapturingDecisionCache(),
            new StubPolicyRepository(
                BuildPolicySet(
                    "ops",
                    "deny-overrides",
                    "permit-readers",
                    "{" +
                    "\"rules\":[{" +
                    "\"id\":\"permit-eng\"," +
                    "\"effect\":\"Permit\"," +
                    "\"conditions\":[{" +
                    "\"path\":\"subject.department\"," +
                    "\"equals\":\"ENG\"}]}]}")),
            new StubPipResolver(AttributeResolutionResult.Succeeded([])),
            new NullAuditWriter());

        var request = BuildRequest(subjectProperties: new Dictionary<string, object?>
        {
            ["department"] = "ENG",
            ["securityClearance"] = BuildClearance()
        });

        var explained = await engine.EvaluateExplainAsync(request with
        {
            Options = new EvaluateOptions { PolicySetId = "ops" }
        });

        Assert.Equal(Decision.Permit, explained.Result.Decision);
        Assert.NotNull(explained.Trace);
        Assert.NotEmpty(explained.Trace.Steps);
        Assert.Contains(explained.Trace.Steps, s => s.RuleId.Contains("spif-resolution"));
        Assert.Contains(explained.Trace.Steps, s => s.RuleId == "permit-eng");
    }

    [Fact]
    public async Task EvaluateBatchAsync_EvaluatesMultipleRequests()
    {
        var spif = BuildSpif();
        var engine = new PdpEngine(
            new AcdfEvaluator(),
            new StubSpifRegistry(spif),
            new CapturingDecisionCache(),
            new StubPolicyRepository(
                BuildPolicySet(
                    "ops",
                    "deny-overrides",
                    "permit-readers",
                    "{\"effect\":\"Permit\"}")),
            new StubPipResolver(AttributeResolutionResult.Succeeded([])),
            new NullAuditWriter());

        var batchRequest = new BatchEvaluationRequest
        {
            RequestId = "batch-1",
            Subject = new SubjectInfo
            {
                Type = "user",
                Id = "alice",
                Properties = new Dictionary<string, object?>
                {
                    ["securityClearance"] = BuildClearance()
                }
            },
            Evaluations =
            [
                new BatchEvaluation
                {
                    EvaluationId = "eval-1",
                    Action = new ActionInfo { Name = "read" },
                    Resource = new ResourceInfo
                    {
                        Type = "document", Id = "doc-1",
                        Properties = new Dictionary<string, object?>
                        {
                            ["securityLabel"] = BuildLabel()
                        }
                    }
                },
                new BatchEvaluation
                {
                    EvaluationId = "eval-2",
                    Action = new ActionInfo { Name = "write" },
                    Resource = new ResourceInfo
                    {
                        Type = "document", Id = "doc-2",
                        Properties = new Dictionary<string, object?>
                        {
                            ["securityLabel"] = BuildLabel()
                        }
                    }
                }
            ]
        };

        var batchResult = await engine.EvaluateBatchAsync(batchRequest);

        Assert.Equal("batch-1", batchResult.RequestId);
        Assert.Equal(2, batchResult.Evaluations.Count);
        Assert.All(batchResult.Evaluations, e => Assert.NotNull(e.DecisionId));
    }

    [Fact]
    public void DecisionCache_UsesArchitectureKeyShapeAndInvalidatesByPolicySet()
    {
        var cache = new DecisionCache(new MemoryCache(new MemoryCacheOptions()));
        var requestA = BuildRequest(subjectId: "alice", includeClearance: true);
        var requestB = BuildRequest(subjectId: "alice", includeClearance: true, label: BuildLabel(classification: 2));

        var keyA = cache.ComputeKey(requestA with { Options = new EvaluateOptions { PolicySetId = "ops" } }, "v1");
        var keyB = cache.ComputeKey(requestB with { Options = new EvaluateOptions { PolicySetId = "ops" } }, "v1");

        Assert.Equal(keyA, keyB);

        cache.Set(keyA, new EvaluationResult
        {
            DecisionId = "d1",
            Decision = Decision.Permit,
            AppliedPolicies = ["policy-set:ops", "policy:permit-readers@v1"]
        }, TimeSpan.FromMinutes(5));

        Assert.True(cache.TryGet(keyA, out _));
        cache.InvalidateByPolicySet("ops");
        Assert.False(cache.TryGet(keyA, out _));
    }

    [Fact]
    public void AcdfEvaluator_EnforcesOnlyOneRequiredCategorySemantics()
    {
        var spif = new SpifIndex(new Spif
        {
            SchemaVersion = "3.0",
            PolicyId = new PolicyInfo { Name = "Test SPIF", Oid = "1.2.3.4" },
            Classifications =
            [
                new SecurityClassification
                {
                    Name = "SECRET",
                    Lacv = 1,
                    Hierarchy = 10,
                    RequiredCategories =
                    [
                        new RequiredCategoryConstraint
                        {
                            Operation = "onlyOne",
                            CategoryGroups =
                            [
                                new CategoryGroupRef { TagSetRef = "SCI", TagType = TagType.Restrictive, Lacv = 100 },
                                new CategoryGroupRef { TagSetRef = "SCI", TagType = TagType.Restrictive, Lacv = 200 }
                            ]
                        }
                    ]
                }
            ],
            CategoryTagSets =
            [
                new SecurityCategoryTagSet
                {
                    TagSetOid = "SCI",
                    Name = "SCI",
                    Tags =
                    [
                        new SecurityCategoryTag
                        {
                            Name = "SCI",
                            TagType = TagType.Restrictive,
                            Categories =
                            [
                                new TagCategory { Name = "ALPHA", Lacv = 100 },
                                new TagCategory { Name = "BETA", Lacv = 200 }
                            ]
                        }
                    ]
                }
            ]
        });

        var label = new SecurityLabel
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacv = 1,
            CategoryTagSets =
            [
                new LabelCategoryTagSet
                {
                    TagSetOid = "SCI",
                    Tags =
                    [
                        new LabelCategoryTag { TagType = TagType.Restrictive, Name = "SCI", Bits = [100, 200] }
                    ]
                }
            ]
        };

        var clearance = new SecurityClearance
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacvs = [1],
            CategoryTagSets =
            [
                new ClearanceCategoryTagSet
                {
                    TagSetOid = "SCI",
                    Tags =
                    [
                        new ClearanceCategoryTag { TagType = TagType.Restrictive, TagOid = "SCI", Bits = [100, 200] }
                    ]
                }
            ]
        };

        var result = new AcdfEvaluator().Evaluate(in label, in clearance, spif);

        Assert.False(result.Pass);
        Assert.Equal(AcdfFailureReason.LabelValidationFailed, result.FailureReason);
    }

    private static EvaluationRequest BuildRequest(
        string subjectId = "alice",
        bool includeClearance = true,
        Dictionary<string, object?>? subjectProperties = null,
        SecurityLabel? label = null)
    {
        var props = subjectProperties ?? new Dictionary<string, object?>();
        if (includeClearance)
        {
            props["securityClearance"] = BuildClearance();
        }

        return new EvaluationRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Subject = new SubjectInfo
            {
                Type = "user",
                Id = subjectId,
                Properties = props
            },
            Action = new ActionInfo { Name = "write" },
            Resource = new ResourceInfo
            {
                Type = "document",
                Id = "doc-1",
                Properties = new Dictionary<string, object?>
                {
                    ["ownerId"] = "bob",
                    ["securityLabel"] = label ?? BuildLabel()
                }
            },
            Context = new ContextInfo()
        };
    }

    private static SecurityLabel BuildLabel(int classification = 1)
        => new()
        {
            PolicyOid = "1.2.3.4",
            ClassificationLacv = classification,
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

    private static PolicySet BuildPolicySet(string id, string combiningAlgorithm, string policyId, string content)
        => new()
        {
            Id = id,
            Name = id,
            CombiningAlgorithm = combiningAlgorithm,
            Policies = [BuildPolicy(policyId, content)]
        };

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
        public AttributeResolutionRequest? LastRequest { get; private set; }

        public Task<AttributeResolutionResult> ResolveAsync(AttributeResolutionRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class NullAuditWriter : IAuditWriter
    {
        public void Write(AuditEvent auditEvent) { }
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CapturingDecisionCache : IDecisionCache
    {
        private readonly Dictionary<string, EvaluationResult> _entries = new(StringComparer.Ordinal);

        public TimeSpan? LastTtl { get; private set; }

        public bool TryGet(string cacheKey, out EvaluationResult? result)
        {
            if (_entries.TryGetValue(cacheKey, out var cached))
            {
                result = cached;
                return true;
            }

            result = null;
            return false;
        }

        public void Set(string cacheKey, EvaluationResult result, TimeSpan ttl)
        {
            _entries[cacheKey] = result;
            LastTtl = ttl;
        }

        public string ComputeKey(EvaluationRequest request, string policyVersion)
            => new DecisionCache(new MemoryCache(new MemoryCacheOptions())).ComputeKey(request, policyVersion);

        public void InvalidateByPolicySet(string policySetId) => _entries.Clear();
        public void InvalidateAll() => _entries.Clear();
    }
}
