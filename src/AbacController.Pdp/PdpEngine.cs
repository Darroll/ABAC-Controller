using System.Diagnostics;
using System.Text.Json;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Labels;
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
    private readonly IAuditWriter _auditWriter;

    public PdpEngine(
        IAcdfEvaluator acdf,
        ISpifRegistry spifRegistry,
        IDecisionCache decisionCache,
        IAuditWriter auditWriter)
    {
        _acdf = acdf;
        _spifRegistry = spifRegistry;
        _decisionCache = decisionCache;
        _auditWriter = auditWriter;
    }

    /// <inheritdoc />
    public Task<EvaluationResult> EvaluateAsync(
        EvaluationRequest request,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var decisionId = Guid.NewGuid().ToString("N");

        var label = ExtractLabel(request);
        var clearance = ExtractClearance(request);

        if (label is null || clearance is null)
        {
            return Task.FromResult(CreateResult(
                decisionId,
                request,
                Decision.Indeterminate,
                sw.Elapsed,
                "Missing security label or clearance in request",
                cacheStatus: "MISS"));
        }

        var spifIndex = ResolveSpif(request, label, clearance);
        if (spifIndex is null)
        {
            return Task.FromResult(CreateResult(
                decisionId,
                request,
                Decision.Indeterminate,
                sw.Elapsed,
                "No applicable SPIF found",
                cacheStatus: "MISS"));
        }

        var policyVersion = request.Options.PolicyVersion
            ?? spifIndex.Spif.Version
            ?? spifIndex.SchemaVersion;

        if (!request.Options.BypassCache)
        {
            var cacheKey = _decisionCache.ComputeKey(request, policyVersion);
            if (_decisionCache.TryGet(cacheKey, out var cached) && cached is not null)
            {
                return Task.FromResult(cached with
                {
                    RequestId = request.RequestId,
                    CacheStatus = "HIT",
                    EvaluationTime = sw.Elapsed
                });
            }
        }

        var acdfResult = _acdf.Evaluate(in label, in clearance, spifIndex);
        sw.Stop();

        var result = CreateResult(
            decisionId,
            request,
            acdfResult.Pass ? Decision.Permit : Decision.Deny,
            sw.Elapsed,
            acdfResult.FailureDetail,
            cacheStatus: request.Options.BypassCache ? "BYPASS" : "MISS",
            appliedPolicies: [spifIndex.PolicyOid]);

        if (!request.Options.BypassCache && result.Decision != Decision.Indeterminate)
        {
            var cacheKey = _decisionCache.ComputeKey(request, policyVersion);
            _decisionCache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
        }

        WriteAuditEvent(result, request);
        return Task.FromResult(result);
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
    public Task<ExplainedEvaluationResult> EvaluateExplainAsync(
        EvaluationRequest request,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var decisionId = Guid.NewGuid().ToString("N");
        var trace = new AcdfTraceCollector();

        var label = ExtractLabel(request);
        var clearance = ExtractClearance(request);

        if (label is null || clearance is null)
        {
            trace.AddStep("input-validation", "FAIL", false, "Missing label or clearance");
            return Task.FromResult(new ExplainedEvaluationResult
            {
                Result = CreateResult(
                    decisionId,
                    request,
                    Decision.Indeterminate,
                    sw.Elapsed,
                    "Missing security label or clearance",
                    cacheStatus: "BYPASS"),
                Trace = new EvaluationTrace { Steps = trace.GetSteps().ToList() }
            });
        }

        var spifIndex = ResolveSpif(request, label, clearance);
        if (spifIndex is null)
        {
            trace.AddStep("spif-resolution", "FAIL", false, "No applicable SPIF found");
            return Task.FromResult(new ExplainedEvaluationResult
            {
                Result = CreateResult(
                    decisionId,
                    request,
                    Decision.Indeterminate,
                    sw.Elapsed,
                    "No applicable SPIF found",
                    cacheStatus: "BYPASS"),
                Trace = new EvaluationTrace { Steps = trace.GetSteps().ToList() }
            });
        }

        trace.AddStep("spif-resolution", "PASS", true,
            $"SPIF resolved: {spifIndex.PolicyName} ({spifIndex.PolicyOid})");
        trace.AddStep("input-validation", "PASS", true, "Label and clearance extracted");

        var acdfResult = _acdf.EvaluateWithTrace(in label, in clearance, spifIndex, trace);
        sw.Stop();

        var result = CreateResult(
            decisionId,
            request,
            acdfResult.Pass ? Decision.Permit : Decision.Deny,
            sw.Elapsed,
            acdfResult.FailureDetail,
            cacheStatus: "BYPASS",
            appliedPolicies: [spifIndex.PolicyOid]);

        WriteAuditEvent(result, request);

        return Task.FromResult(new ExplainedEvaluationResult
        {
            Result = result,
            Trace = new EvaluationTrace
            {
                PolicySetId = spifIndex.PolicyOid,
                PolicyVersion = request.Options.PolicyVersion ?? spifIndex.Spif.Version ?? spifIndex.SchemaVersion,
                MatchedPolicy = spifIndex.PolicyName,
                Steps = trace.GetSteps().ToList()
            }
        });
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
        List<string>? appliedPolicies = null)
    {
        var code = decision == Decision.Indeterminate ? "error" : "ok";

        return new EvaluationResult
        {
            RequestId = request.RequestId,
            DecisionId = decisionId,
            Decision = decision,
            Status = message is null ? null : new StatusInfo { Code = code, Message = message },
            AppliedPolicies = appliedPolicies ?? [],
            EvaluationTime = elapsed,
            CacheStatus = cacheStatus
        };
    }

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
            EvaluationTimeMs = result.EvaluationTime.TotalMilliseconds
        });
    }
}
