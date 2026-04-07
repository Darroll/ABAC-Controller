using AbacController.Core.Domain.Decisions;

namespace AbacController.Core.Domain.Classifications;

/// <summary>
/// Query for allowed classifications given a subject, application, and context.
/// The PDP evaluates SPIF classifications through three filter layers:
/// clearance, native policy, and application scope.
/// </summary>
public sealed record ClassificationAssignmentQuery
{
    /// <summary>Caller-provided request ID for correlation.</summary>
    public string? RequestId { get; init; }

    /// <summary>Subject being evaluated (user or service principal).</summary>
    public required SubjectInfo Subject { get; init; }

    /// <summary>
    /// Optional application scope filter. When set, loads the
    /// <see cref="ApplicationRegistration"/> and applies its classification constraints.
    /// </summary>
    public string? ApplicationId { get; init; }

    /// <summary>
    /// Explicit SPIF policy OID override. Takes precedence over
    /// application default and tenant default.
    /// </summary>
    public string? PolicyOidOverride { get; init; }

    /// <summary>
    /// Action name for native policy evaluation context.
    /// Defaults to "classify".
    /// </summary>
    public string ActionName { get; init; } = "classify";

    /// <summary>Optional resource context for policy evaluation.</summary>
    public ResourceContext? Resource { get; init; }

    /// <summary>Optional environment attributes for policy evaluation.</summary>
    public Dictionary<string, object?> Environment { get; init; } = new();

    /// <summary>Include SPIF marking data in each allowed classification.</summary>
    public bool IncludeMarkingData { get; init; }

    /// <summary>Include allowed category tag sets per classification.</summary>
    public bool IncludeCategories { get; init; } = true;

    /// <summary>Include diagnostic filter trace in the result.</summary>
    public bool IncludeTrace { get; init; }

    /// <summary>Target a specific policy set for native policy evaluation.</summary>
    public string? PolicySetId { get; init; }
}

/// <summary>
/// Lightweight resource context for classification assignment queries.
/// </summary>
public sealed record ResourceContext
{
    /// <summary>Resource type (e.g., "email", "document").</summary>
    public string? Type { get; init; }

    /// <summary>Resource identifier.</summary>
    public string? Id { get; init; }

    /// <summary>Additional resource properties.</summary>
    public Dictionary<string, object?> Properties { get; init; } = new();
}
