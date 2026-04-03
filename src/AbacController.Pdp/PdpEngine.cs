using System.Diagnostics;
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
    public async Task<EvaluationResult> EvaluateAsync(
        EvaluationRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var decisionId = Guid.NewGuid().ToString("N");

        // Step 1: Check decision cache
        if (!request.Options.BypassCache)
        {
            var cacheKey = _decisionCache.ComputeKey(request, "current");
            if (_decisionCache.TryGet(cacheKey, out var cached) && cached is not null)
            {
                return cached with
                {
                    RequestId = request.RequestId,
                    CacheStatus = "HIT",
                    EvaluationTime = sw.Elapsed
                };
            }
        }

        // Step 2: Resolve governing SPIF (REQ-PDP-027)
        var spifIndex = ResolveSpif(request);
        if (spifIndex is null)
        {
            return CreateDenyResult(decisionId, request, sw.Elapsed,
                "No applicable SPIF found", Decision.Indeterminate);
        }

        // Step 3: Extract label and clearance from request
        var label = ExtractLabel(request);
        var clearance = ExtractClearance(request);

        if (label is null || clearance is null)
        {
            return CreateDenyResult(decisionId, request, sw.Elapsed,
                "Missing security label or clearance in request", Decision.Indeterminate);
        }

        // Step 4: Execute ACDF
        var acdfResult = _acdf.Evaluate(in label, in clearance, spifIndex);

        Decision decision;
        string? statusMessage = null;

        if (acdfResult.Pass)
        {
            decision = Decision.Permit;
        }
        else
        {
            decision = Decision.Deny;
            statusMessage = acdfResult.FailureDetail;
        }

        sw.Stop();
        var result = new EvaluationResult
        {
            RequestId = request.RequestId,
            DecisionId = decisionId,
            Decision = decision,
            Status = statusMessage is not null ? new StatusInfo { Code = "ok", Message = statusMessage } : null,
            AppliedPolicies = [spifIndex.PolicyOid],
            EvaluationTime = sw.Elapsed,
            CacheStatus = "MISS"
        };

        // Step 5: Cache result
        if (!request.Options.BypassCache && decision != Decision.Indeterminate)
        {
            var cacheKey = _decisionCache.ComputeKey(request, "current");
            _decisionCache.Set(cacheKey, result, TimeSpan.FromSeconds(300));
        }

        // Step 6: Audit (non-blocking)
        WriteAuditEvent(result, request);

        return result;
    }

    /// <inheritdoc />
    public async Task<BatchEvaluationResult> EvaluateBatchAsync(
        BatchEvaluationRequest request, CancellationToken ct = default)
    {
        var results = new List<EvaluationResult>();

        foreach (var eval in request.Evaluations)
        {
            var singleRequest = new EvaluationRequest
            {
                RequestId = eval.EvaluationId,
                Subject = request.Subject,
                Action = eval.Action,
                Resource = eval.Resource,
                Context = request.Context,
                Options = request.Options
            };

            var result = await EvaluateAsync(singleRequest, ct);
            results.Add(result);
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
        EvaluationRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var decisionId = Guid.NewGuid().ToString("N");
        var trace = new AcdfTraceCollector();

        var spifIndex = ResolveSpif(request);
        if (spifIndex is null)
        {
            var denyResult = CreateDenyResult(decisionId, request, sw.Elapsed,
                "No applicable SPIF found", Decision.Indeterminate);
            trace.AddStep("spif-resolution", "FAIL", false, "No applicable SPIF found");
            return new ExplainedEvaluationResult
            {
                Result = denyResult,
                Trace = new EvaluationTrace { Steps = trace.GetSteps().ToList() }
            };
        }

        trace.AddStep("spif-resolution", "PASS", true,
            $"SPIF resolved: {spifIndex.PolicyName} ({spifIndex.PolicyOid})");

        var label = ExtractLabel(request);
        var clearance = ExtractClearance(request);

        if (label is null || clearance is null)
        {
            var denyResult = CreateDenyResult(decisionId, request, sw.Elapsed,
                "Missing security label or clearance", Decision.Indeterminate);
            trace.AddStep("input-validation", "FAIL", false, "Missing label or clearance");
            return new ExplainedEvaluationResult
            {
                Result = denyResult,
                Trace = new EvaluationTrace { Steps = trace.GetSteps().ToList() }
            };
        }

        trace.AddStep("input-validation", "PASS", true, "Label and clearance extracted");

        var acdfResult = _acdf.EvaluateWithTrace(in label, in clearance, spifIndex, trace);

        sw.Stop();
        var result = new EvaluationResult
        {
            RequestId = request.RequestId,
            DecisionId = decisionId,
            Decision = acdfResult.Pass ? Decision.Permit : Decision.Deny,
            Status = acdfResult.FailureDetail is not null
                ? new StatusInfo { Code = "ok", Message = acdfResult.FailureDetail }
                : null,
            AppliedPolicies = [spifIndex.PolicyOid],
            EvaluationTime = sw.Elapsed,
            CacheStatus = "BYPASS"
        };

        WriteAuditEvent(result, request);

        return new ExplainedEvaluationResult
        {
            Result = result,
            Trace = new EvaluationTrace
            {
                PolicySetId = spifIndex.PolicyOid,
                Steps = trace.GetSteps().ToList()
            }
        };
    }

    /// <summary>
    /// Resolve the governing SPIF per REQ-PDP-027:
    /// (1) label-embedded OID → (2) caller override → (3) system default.
    /// </summary>
    private ISpifIndex? ResolveSpif(EvaluationRequest request)
    {
        // Try label-embedded policy OID from resource properties
        if (request.Resource.Properties.TryGetValue("securityLabel.policyOid", out var policyOidObj)
            && policyOidObj is string policyOid
            && !string.IsNullOrEmpty(policyOid))
        {
            var index = _spifRegistry.GetByPolicyOid(policyOid);
            if (index is not null) return index;
        }

        // Try caller override
        if (!string.IsNullOrEmpty(request.Options.PolicyIdOverride))
        {
            var index = _spifRegistry.GetByPolicyOid(request.Options.PolicyIdOverride);
            if (index is not null) return index;
        }

        // Fall back to system default
        return _spifRegistry.GetDefault();
    }

    private static SecurityLabel? ExtractLabel(EvaluationRequest request)
    {
        if (request.Resource.Properties.TryGetValue("securityLabel", out var labelObj)
            && labelObj is SecurityLabel label)
        {
            return label;
        }
        return null;
    }

    private static SecurityClearance? ExtractClearance(EvaluationRequest request)
    {
        if (request.Subject.Properties.TryGetValue("securityClearance", out var clearanceObj)
            && clearanceObj is SecurityClearance clearance)
        {
            return clearance;
        }
        return null;
    }

    private static EvaluationResult CreateDenyResult(
        string decisionId, EvaluationRequest request, TimeSpan elapsed,
        string message, Decision decision)
    {
        return new EvaluationResult
        {
            RequestId = request.RequestId,
            DecisionId = decisionId,
            Decision = decision,
            Status = new StatusInfo { Code = "error", Message = message },
            EvaluationTime = elapsed,
            CacheStatus = "MISS"
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
            AppliedPolicies = System.Text.Json.JsonSerializer.Serialize(result.AppliedPolicies),
            EvaluationTimeMs = result.EvaluationTime.TotalMilliseconds
        });
    }
}
