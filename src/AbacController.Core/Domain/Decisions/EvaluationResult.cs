using AbacController.Core.Constants;

namespace AbacController.Core.Domain.Decisions;

/// <summary>
/// Result of a PDP evaluation.
/// </summary>
public sealed record EvaluationResult
{
    /// <summary>Caller-provided request ID.</summary>
    public string? RequestId { get; init; }

    /// <summary>Generated decision ID.</summary>
    public required string DecisionId { get; init; }

    /// <summary>XACML 4-valued decision.</summary>
    public required Decision Decision { get; init; }

    /// <summary>Status information.</summary>
    public StatusInfo? Status { get; init; }

    /// <summary>Obligations the PEP must fulfill.</summary>
    public List<Obligation> Obligations { get; init; } = [];

    /// <summary>Advice (optional actions the PEP may take).</summary>
    public List<Advice> Advice { get; init; } = [];

    /// <summary>Policies that contributed to this decision.</summary>
    public List<string> AppliedPolicies { get; init; } = [];

    /// <summary>Evaluation duration.</summary>
    public TimeSpan EvaluationTime { get; init; }

    /// <summary>Cache status: HIT, MISS, or BYPASS.</summary>
    public string CacheStatus { get; init; } = "MISS";

    /// <summary>Attribute provenance information.</summary>
    public List<AttributeProvenance> AttributeProvenance { get; init; } = [];
}

/// <summary>
/// Batch evaluation result.
/// </summary>
public sealed record BatchEvaluationResult
{
    /// <summary>Caller-provided request ID.</summary>
    public string? RequestId { get; init; }

    /// <summary>Batch decision ID.</summary>
    public required string BatchDecisionId { get; init; }

    /// <summary>Individual evaluation results.</summary>
    public required List<EvaluationResult> Evaluations { get; init; }
}

/// <summary>
/// Evaluation result with full explanation trace.
/// </summary>
public sealed record ExplainedEvaluationResult
{
    /// <summary>The evaluation result.</summary>
    public required EvaluationResult Result { get; init; }

    /// <summary>Step-by-step reasoning trace.</summary>
    public required EvaluationTrace Trace { get; init; }
}

/// <summary>
/// Status information for an evaluation.
/// </summary>
public sealed record StatusInfo
{
    /// <summary>Status code (e.g. "ok", "missing-attribute", "processing-error").</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable status message.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// An obligation the PEP must fulfill.
/// </summary>
public sealed record Obligation
{
    /// <summary>Obligation identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Obligation attributes the PEP must apply.</summary>
    public Dictionary<string, object?> Attributes { get; init; } = new();
}

/// <summary>
/// Advice the PEP may optionally follow.
/// </summary>
public sealed record Advice
{
    /// <summary>Advice identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Advice attributes the PEP may optionally apply.</summary>
    public Dictionary<string, object?> Attributes { get; init; } = new();
}

/// <summary>
/// Records which attribute was resolved from which source.
/// </summary>
public sealed record AttributeProvenance
{
    /// <summary>Attribute name.</summary>
    public required string AttributeName { get; init; }

    /// <summary>Source that provided the value.</summary>
    public required string Source { get; init; }

    /// <summary>When the value was fetched.</summary>
    public DateTimeOffset FetchedAt { get; init; }

    /// <summary>Source TTL for cache computation.</summary>
    public TimeSpan? SourceTtl { get; init; }
}

/// <summary>
/// Step-by-step evaluation trace for explain mode.
/// </summary>
public sealed record EvaluationTrace
{
    /// <summary>Policy set that was evaluated.</summary>
    public string? PolicySetId { get; init; }

    /// <summary>Version of the evaluated policy.</summary>
    public string? PolicyVersion { get; init; }

    /// <summary>Specific policy rule that matched.</summary>
    public string? MatchedPolicy { get; init; }

    /// <summary>Ordered trace steps showing rule-by-rule evaluation.</summary>
    public List<TraceStep> Steps { get; init; } = [];
}

/// <summary>
/// A single step in an evaluation trace.
/// </summary>
public sealed record TraceStep
{
    /// <summary>Identifier of the evaluated rule.</summary>
    public required string RuleId { get; init; }

    /// <summary>Effect of the rule (Permit or Deny).</summary>
    public required string Effect { get; init; }

    /// <summary>Whether the rule condition matched.</summary>
    public required bool Result { get; init; }

    /// <summary>Human-readable explanation of this evaluation step.</summary>
    public required string Reason { get; init; }
}
