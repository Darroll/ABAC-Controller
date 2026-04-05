using System.Diagnostics;
using System.Text.Json;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Attributes;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Policy;
using AbacController.Core.Interfaces;

namespace AbacController.Pdp;

/// <summary>
/// PDP engine orchestrator. Manages the full evaluation flow:
/// cache check → SPIF resolution → attribute resolution → ACDF → policy evaluation → audit.
/// </summary>
public sealed class PdpEngine : IPdpEngine
{
    private readonly IAcdfEvaluator _acdf;
    private readonly ISpifRegistry _spifRegistry;
    private readonly IDecisionCache _decisionCache;
    private readonly IPolicyRepository _policyRepository;
    private readonly IPipResolver _pipResolver;
    private readonly IAuditWriter _auditWriter;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes a new instance of the <see cref="PdpEngine"/> class.</summary>
    public PdpEngine(
        IAcdfEvaluator acdf,
        ISpifRegistry spifRegistry,
        IDecisionCache decisionCache,
        IPolicyRepository policyRepository,
        IPipResolver pipResolver,
        IAuditWriter auditWriter,
        ITenantContext? tenantContext = null)
    {
        _acdf = acdf;
        _spifRegistry = spifRegistry;
        _decisionCache = decisionCache;
        _policyRepository = policyRepository;
        _pipResolver = pipResolver;
        _auditWriter = auditWriter;
        _tenantContext = tenantContext ?? NullTenantContext.Instance;
    }

    /// <inheritdoc />
    public async Task<EvaluationResult> EvaluateAsync(
        EvaluationRequest request,
        CancellationToken ct = default)
    {
        var evaluation = await EvaluateInternalAsync(request, explain: false, ct);
        return evaluation.Result;
    }

    /// <inheritdoc />
    public async Task<BatchEvaluationResult> EvaluateBatchAsync(
        BatchEvaluationRequest request,
        CancellationToken ct = default)
    {
        var results = new List<EvaluationResult>(request.Evaluations.Count);

        foreach (var eval in request.Evaluations)
        {
            ct.ThrowIfCancellationRequested();

            var singleRequest = new EvaluationRequest
            {
                RequestId = eval.EvaluationId,
                Subject = request.Subject,
                Action = eval.Action,
                Resource = eval.Resource,
                Context = request.Context,
                Options = request.Options
            };

            results.Add(await EvaluateAsync(singleRequest, ct));
        }

        return new BatchEvaluationResult
        {
            RequestId = request.RequestId,
            BatchDecisionId = Guid.NewGuid().ToString("N"),
            Evaluations = results
        };
    }

    /// <inheritdoc />
    public async Task<ExplainedEvaluationResult> EvaluateExplainAsync(
        EvaluationRequest request,
        CancellationToken ct = default)
    {
        var evaluation = await EvaluateInternalAsync(request, explain: true, ct);
        return new ExplainedEvaluationResult
        {
            Result = evaluation.Result,
            Trace = evaluation.Trace ?? new EvaluationTrace()
        };
    }

    private async Task<InternalEvaluation> EvaluateInternalAsync(
        EvaluationRequest request,
        bool explain,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var decisionId = Guid.NewGuid().ToString("N");
        var traceCollector = explain ? new AcdfTraceCollector() : null;
        var cacheStatus = explain || request.Options.BypassCache ? "BYPASS" : "MISS";

        var label = ExtractLabel(request);
        if (label is null)
        {
            traceCollector?.AddStep("input-validation", "FAIL", false, "Missing security label in request");
            var missingLabelResult = CreateResult(
                decisionId,
                request,
                Decision.Indeterminate,
                sw.Elapsed,
                "Missing security label in request",
                cacheStatus);
            WriteAuditEvent(missingLabelResult, request);
            return BuildInternalEvaluation(missingLabelResult, traceCollector, request.Options.PolicySetId, null, null);
        }

        var spifIndex = ResolveSpif(request, label, clearance: null);
        if (spifIndex is null)
        {
            traceCollector?.AddStep("spif-resolution", "FAIL", false, "No applicable SPIF found");
            var missingSpifResult = CreateResult(
                decisionId,
                request,
                Decision.Indeterminate,
                sw.Elapsed,
                "No applicable SPIF found",
                cacheStatus);
            WriteAuditEvent(missingSpifResult, request);
            return BuildInternalEvaluation(missingSpifResult, traceCollector, request.Options.PolicySetId, null, null);
        }

        traceCollector?.AddStep("spif-resolution", "PASS", true,
            $"SPIF resolved: {spifIndex.PolicyName} ({spifIndex.PolicyOid})");

        var policySets = await LoadPolicySetsAsync(request, ct);
        var policyVersion = NativePolicyEvaluator.ComputeVersionFingerprint(policySets, request.Options.PolicyVersion);

        if (!explain && !request.Options.BypassCache)
        {
            var cacheKey = ComputeTenantScopedCacheKey(request, policyVersion);
            if (_decisionCache.TryGet(cacheKey, out var cached) && cached is not null)
            {
                var cacheHit = cached with
                {
                    RequestId = request.RequestId,
                    CacheStatus = "HIT",
                    EvaluationTime = sw.Elapsed
                };
                WriteAuditEvent(cacheHit, request);
                return new InternalEvaluation(cacheHit, null);
            }
        }

        var enrichment = await EnrichRequestAsync(request, policySets, ct);
        var evaluationRequest = enrichment.Request;
        var provenance = BuildProvenance(enrichment.ResolvedAttributes);

        traceCollector?.AddStep(
            "pip-resolution",
            enrichment.MissingAttributes.Count == 0 ? "PASS" : "PARTIAL",
            enrichment.MissingAttributes.Count == 0,
            enrichment.ResolvedAttributes.Count == 0
                ? "No PIP attributes resolved"
                : $"Resolved attributes: {string.Join(", ", enrichment.ResolvedAttributes.Select(static a => a.Name))}");

        var clearance = ExtractClearance(evaluationRequest);
        if (clearance is null)
        {
            var missingMessage = enrichment.MissingAttributes.Count > 0
                ? $"Missing required attributes: {string.Join(", ", enrichment.MissingAttributes)}"
                : "Missing security clearance in request";

            traceCollector?.AddStep("input-validation", "FAIL", false, missingMessage);

            var indeterminate = CreateResult(
                decisionId,
                evaluationRequest,
                Decision.Indeterminate,
                sw.Elapsed,
                missingMessage,
                cacheStatus,
                attributeProvenance: provenance);
            WriteAuditEvent(indeterminate, evaluationRequest);
            return BuildInternalEvaluation(indeterminate, traceCollector, request.Options.PolicySetId, policyVersion, spifIndex.PolicyName);
        }

        traceCollector?.AddStep("input-validation", "PASS", true, "Security label and clearance available");

        var acdfResult = explain
            ? _acdf.EvaluateWithTrace(in label, in clearance, spifIndex, traceCollector!)
            : _acdf.Evaluate(in label, in clearance, spifIndex);

        if (!acdfResult.Pass)
        {
            var denied = CreateResult(
                decisionId,
                evaluationRequest,
                Decision.Deny,
                sw.Elapsed,
                acdfResult.FailureDetail,
                cacheStatus,
                attributeProvenance: provenance);
            WriteAuditEvent(denied, evaluationRequest);
            return BuildInternalEvaluation(denied, traceCollector, request.Options.PolicySetId, policyVersion, spifIndex.PolicyName);
        }

        var policyOutcome = NativePolicyEvaluator.Evaluate(evaluationRequest, policySets, request.Options.PolicyVersion);
        foreach (var step in policyOutcome.TraceSteps)
        {
            traceCollector?.AddStep(step.RuleId, step.Effect, step.Result, step.Reason);
        }

        // Preserve full 4-valued semantics (Permit/Deny/NotApplicable/Indeterminate)
        // per NIST 800-162 and XACML. Callers interpret NotApplicable as needed.
        var finalDecision = policyOutcome.Decision;

        var message = policyOutcome.Decision == Decision.NotApplicable
            ? "No applicable policy matched"
            : policyOutcome.Message;

        var result = CreateResult(
            decisionId,
            evaluationRequest,
            finalDecision,
            sw.Elapsed,
            message,
            cacheStatus,
            appliedPolicies: policyOutcome.AppliedPolicies,
            obligations: request.Options.ReturnObligations ? policyOutcome.Obligations : null,
            advice: request.Options.ReturnAdvice ? policyOutcome.Advice : null,
            attributeProvenance: provenance);

        if (!explain && !request.Options.BypassCache && result.Decision != Decision.Indeterminate)
        {
            var cacheKey = ComputeTenantScopedCacheKey(request, policyVersion);
            _decisionCache.Set(cacheKey, result, ComputeDecisionTtl(enrichment.ResolvedAttributes));
        }

        WriteAuditEvent(result, evaluationRequest);
        return BuildInternalEvaluation(result, traceCollector, request.Options.PolicySetId, policyVersion, spifIndex.PolicyName);
    }

    /// <summary>
    /// Resolve the governing SPIF per REQ-PDP-027:
    /// (1) label-embedded OID → (2) caller override → (3) clearance policy → (4) system default.
    /// </summary>
    private ISpifIndex? ResolveSpif(
        EvaluationRequest request,
        SecurityLabel? label,
        SecurityClearance? clearance)
    {
        if (!string.IsNullOrWhiteSpace(label?.PolicyOid))
        {
            var fromLabel = _spifRegistry.GetByPolicyOid(label.PolicyOid!);
            if (fromLabel is not null)
            {
                return fromLabel;
            }
        }

        if (request.Resource.Properties.TryGetValue("securityLabel.policyOid", out var policyOidObj) &&
            policyOidObj is string policyOid &&
            !string.IsNullOrWhiteSpace(policyOid))
        {
            var fromProperty = _spifRegistry.GetByPolicyOid(policyOid);
            if (fromProperty is not null)
            {
                return fromProperty;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Options.PolicyIdOverride))
        {
            var fromOverride = _spifRegistry.GetByPolicyOid(request.Options.PolicyIdOverride!);
            if (fromOverride is not null)
            {
                return fromOverride;
            }
        }

        if (!string.IsNullOrWhiteSpace(clearance?.PolicyOid))
        {
            var fromClearance = _spifRegistry.GetByPolicyOid(clearance.PolicyOid);
            if (fromClearance is not null)
            {
                return fromClearance;
            }
        }

        return _spifRegistry.GetDefault();
    }

    private async Task<List<PolicySet>> LoadPolicySetsAsync(EvaluationRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Options.PolicySetId))
        {
            var policySet = await _policyRepository.GetPolicySetAsync(request.Options.PolicySetId!, ct);
            return policySet is null || !policySet.IsActive ? [] : [policySet];
        }

        return (await _policyRepository.GetPolicySetsAsync(ct))
            .Where(static ps => ps.IsActive)
            .ToList();
    }

    private async Task<RequestEnrichment> EnrichRequestAsync(
        EvaluationRequest request,
        IReadOnlyList<PolicySet> policySets,
        CancellationToken ct)
    {
        var requiredAttributes = new HashSet<string>(StringComparer.Ordinal);

        if (!request.Subject.Properties.ContainsKey("securityClearance"))
        {
            requiredAttributes.Add("securityClearance");
        }

        foreach (var attribute in NativePolicyEvaluator.CollectRequiredSubjectAttributes(policySets, request.Options.PolicyVersion))
        {
            if (!request.Subject.Properties.ContainsKey(attribute))
            {
                requiredAttributes.Add(attribute);
            }
        }

        if (requiredAttributes.Count == 0)
        {
            return new RequestEnrichment(request, [], []);
        }

        var resolution = await _pipResolver.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = request.Subject.Id,
            SubjectType = request.Subject.Type,
            RequestedAttributes = requiredAttributes.ToList(),
            Context = new Dictionary<string, object?>
            {
                ["actionName"] = request.Action.Name,
                ["resourceType"] = request.Resource.Type,
                ["resourceId"] = request.Resource.Id
            }
        }, ct);

        var subjectProperties = new Dictionary<string, object?>(request.Subject.Properties, StringComparer.Ordinal);
        var resourceProperties = new Dictionary<string, object?>(request.Resource.Properties, StringComparer.Ordinal);
        var actionProperties = new Dictionary<string, object?>(request.Action.Properties, StringComparer.Ordinal);
        var environment = new Dictionary<string, object?>(request.Context?.Environment ?? [], StringComparer.Ordinal);

        foreach (var value in resolution.Values)
        {
            switch (value.Category)
            {
                case AttributeCategory.Subject:
                    subjectProperties[value.Name] = value.Value;
                    break;
                case AttributeCategory.Resource:
                    resourceProperties[value.Name] = value.Value;
                    break;
                case AttributeCategory.Action:
                    actionProperties[value.Name] = value.Value;
                    break;
                case AttributeCategory.Environment:
                    environment[value.Name] = value.Value;
                    break;
            }
        }

        var enrichedRequest = request with
        {
            Subject = request.Subject with { Properties = subjectProperties },
            Resource = request.Resource with { Properties = resourceProperties },
            Action = request.Action with { Properties = actionProperties },
            Context = request.Context is null
                ? new ContextInfo { Environment = environment }
                : request.Context with { Environment = environment }
        };

        return new RequestEnrichment(enrichedRequest, resolution.Values, resolution.Missing);
    }

    private static SecurityLabel? ExtractLabel(EvaluationRequest request)
        => request.Resource.Properties.TryGetValue("securityLabel", out var labelObj) && labelObj is SecurityLabel label
            ? label
            : null;

    private static SecurityClearance? ExtractClearance(EvaluationRequest request)
        => request.Subject.Properties.TryGetValue("securityClearance", out var clearanceObj) &&
           clearanceObj is SecurityClearance clearance
            ? clearance
            : null;

    private static EvaluationResult CreateResult(
        string decisionId,
        EvaluationRequest request,
        Decision decision,
        TimeSpan elapsed,
        string? message,
        string cacheStatus,
        List<string>? appliedPolicies = null,
        List<Obligation>? obligations = null,
        List<Advice>? advice = null,
        List<AttributeProvenance>? attributeProvenance = null)
    {
        var code = decision == Decision.Indeterminate ? "error" : "ok";

        return new EvaluationResult
        {
            RequestId = request.RequestId,
            DecisionId = decisionId,
            Decision = decision,
            Status = message is null ? null : new StatusInfo { Code = code, Message = message },
            Obligations = obligations ?? [],
            Advice = advice ?? [],
            AppliedPolicies = appliedPolicies ?? [],
            EvaluationTime = elapsed,
            CacheStatus = cacheStatus,
            AttributeProvenance = attributeProvenance ?? []
        };
    }

    private string ComputeTenantScopedCacheKey(EvaluationRequest request, string policyVersion)
    {
        var cacheKey = _decisionCache.ComputeKey(request, policyVersion);
        return $"{_tenantContext.TenantId ?? "default"}:{cacheKey}";
    }

    private static TimeSpan ComputeDecisionTtl(IReadOnlyList<AttributeValue> resolvedAttributes)
    {
        var configuredTtl = TimeSpan.FromMinutes(5);
        var minAttributeTtl = resolvedAttributes
            .Where(static a => a.CacheTtl.HasValue)
            .Select(static a => a.CacheTtl!.Value)
            .DefaultIfEmpty(configuredTtl)
            .Min();

        return minAttributeTtl < configuredTtl ? minAttributeTtl : configuredTtl;
    }

    private static List<AttributeProvenance> BuildProvenance(IReadOnlyList<AttributeValue> resolvedAttributes)
        => resolvedAttributes.Select(static value => new AttributeProvenance
        {
            AttributeName = value.Name,
            Source = value.SourceId,
            FetchedAt = value.FetchedAt,
            SourceTtl = value.CacheTtl
        }).ToList();

    private static InternalEvaluation BuildInternalEvaluation(
        EvaluationResult result,
        AcdfTraceCollector? traceCollector,
        string? policySetId,
        string? policyVersion,
        string? matchedPolicy)
        => new(
            result,
            traceCollector is null
                ? null
                : new EvaluationTrace
                {
                    PolicySetId = policySetId,
                    PolicyVersion = policyVersion,
                    MatchedPolicy = matchedPolicy,
                    Steps = traceCollector.GetSteps().ToList()
                });

    private void WriteAuditEvent(EvaluationResult result, EvaluationRequest request)
    {
        _auditWriter.Write(new Core.Domain.Audit.AuditEvent
        {
            EventType = "evaluation",
            RequestId = result.RequestId,
            DecisionId = result.DecisionId,
            SubjectType = request.Subject.Type,
            SubjectId = request.Subject.Id,
            ActionName = request.Action.Name,
            ResourceType = request.Resource.Type,
            ResourceId = request.Resource.Id,
            Decision = result.Decision.ToString(),
            AppliedPolicies = JsonSerializer.Serialize(result.AppliedPolicies),
            ObligationsJson = result.Obligations.Count > 0 ? JsonSerializer.Serialize(result.Obligations) : null,
            AttributesUsedJson = result.AttributeProvenance.Count > 0 ? JsonSerializer.Serialize(result.AttributeProvenance) : null,
            EvaluationTimeMs = result.EvaluationTime.TotalMilliseconds,
            TenantId = _tenantContext.TenantId
        });
    }

    private sealed record RequestEnrichment(
        EvaluationRequest Request,
        List<AttributeValue> ResolvedAttributes,
        List<string> MissingAttributes);

    private sealed record InternalEvaluation(EvaluationResult Result, EvaluationTrace? Trace);

    private sealed class NullTenantContext : ITenantContext
    {
        /// <summary>Gets the Instance field.</summary>
        public static readonly NullTenantContext Instance = new();

        /// <summary>Gets the tenant identifier.</summary>
        public string? TenantId => null;
    }
}
