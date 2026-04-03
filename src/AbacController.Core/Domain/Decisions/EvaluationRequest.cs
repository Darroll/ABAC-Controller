namespace AbacController.Core.Domain.Decisions;

/// <summary>
/// An authorization evaluation request sent to the PDP.
/// </summary>
public sealed record EvaluationRequest
{
    /// <summary>Caller-provided request ID for correlation.</summary>
    public string? RequestId { get; init; }

    /// <summary>Subject requesting access.</summary>
    public required SubjectInfo Subject { get; init; }

    /// <summary>Action being requested.</summary>
    public required ActionInfo Action { get; init; }

    /// <summary>Resource being accessed.</summary>
    public required ResourceInfo Resource { get; init; }

    /// <summary>Environmental context.</summary>
    public ContextInfo? Context { get; init; }

    /// <summary>Evaluation options.</summary>
    public EvaluateOptions Options { get; init; } = new();
}

/// <summary>
/// Subject information for evaluation.
/// </summary>
public sealed record SubjectInfo
{
    /// <summary>Subject type (e.g., "user", "service").</summary>
    public required string Type { get; init; }

    /// <summary>Subject identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Additional subject properties.</summary>
    public Dictionary<string, object?> Properties { get; init; } = new();
}

/// <summary>
/// Action information for evaluation.
/// </summary>
public sealed record ActionInfo
{
    /// <summary>Action name (e.g., "read", "write", "execute").</summary>
    public required string Name { get; init; }

    /// <summary>Additional action properties.</summary>
    public Dictionary<string, object?> Properties { get; init; } = new();
}

/// <summary>
/// Resource information for evaluation.
/// </summary>
public sealed record ResourceInfo
{
    /// <summary>Resource type.</summary>
    public required string Type { get; init; }

    /// <summary>Resource identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Additional resource properties.</summary>
    public Dictionary<string, object?> Properties { get; init; } = new();
}

/// <summary>
/// Environmental context for evaluation.
/// </summary>
public sealed record ContextInfo
{
    /// <summary>Environment variables (time, location, etc.).</summary>
    public Dictionary<string, object?> Environment { get; init; } = new();
}

/// <summary>
/// Options for evaluation requests.
/// </summary>
public sealed record EvaluateOptions
{
    /// <summary>Return obligations with decision.</summary>
    public bool ReturnObligations { get; init; }

    /// <summary>Return advice with decision.</summary>
    public bool ReturnAdvice { get; init; }

    /// <summary>Policy set to evaluate against.</summary>
    public string? PolicySetId { get; init; }

    /// <summary>Specific policy version to use.</summary>
    public string? PolicyVersion { get; init; }

    /// <summary>Skip decision cache.</summary>
    public bool BypassCache { get; init; }

    /// <summary>Caller-supplied SPIF override (REQ-PDP-027).</summary>
    public string? PolicyIdOverride { get; init; }
}

/// <summary>
/// Batch evaluation request with shared subject.
/// </summary>
public sealed record BatchEvaluationRequest
{
    /// <summary>Caller-provided request ID.</summary>
    public string? RequestId { get; init; }

    /// <summary>Shared subject for all evaluations.</summary>
    public required SubjectInfo Subject { get; init; }

    /// <summary>Shared context.</summary>
    public ContextInfo? Context { get; init; }

    /// <summary>Individual evaluations.</summary>
    public required List<BatchEvaluation> Evaluations { get; init; }

    /// <summary>Shared options.</summary>
    public EvaluateOptions Options { get; init; } = new();
}

/// <summary>
/// A single evaluation within a batch.
/// </summary>
public sealed record BatchEvaluation
{
    /// <summary>Evaluation ID within the batch.</summary>
    public required string EvaluationId { get; init; }

    /// <summary>Action being requested.</summary>
    public required ActionInfo Action { get; init; }

    /// <summary>Resource being accessed.</summary>
    public required ResourceInfo Resource { get; init; }
}
