namespace AbacController.Core.Domain.Audit;

/// <summary>
/// An audit event recording a decision or administrative action.
/// Append-only — once created, never modified or deleted.
/// </summary>
public sealed record AuditEvent
{
    /// <summary>Unique event identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Event timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Event type: "evaluation", "policy_change", "spif_import", etc.</summary>
    public required string EventType { get; init; }

    /// <summary>Caller-provided request ID.</summary>
    public string? RequestId { get; init; }

    /// <summary>Decision ID (for evaluation events).</summary>
    public string? DecisionId { get; init; }

    /// <summary>Subject type.</summary>
    public string? SubjectType { get; init; }

    /// <summary>Subject identifier.</summary>
    public string? SubjectId { get; init; }

    /// <summary>Action name.</summary>
    public string? ActionName { get; init; }

    /// <summary>Resource type.</summary>
    public string? ResourceType { get; init; }

    /// <summary>Resource identifier.</summary>
    public string? ResourceId { get; init; }

    /// <summary>Decision outcome.</summary>
    public string? Decision { get; init; }

    /// <summary>Applied policies (JSON array).</summary>
    public string? AppliedPolicies { get; init; }

    /// <summary>Obligations (JSON).</summary>
    public string? ObligationsJson { get; init; }

    /// <summary>Attributes used with provenance (JSON).</summary>
    public string? AttributesUsedJson { get; init; }

    /// <summary>Evaluation time in milliseconds.</summary>
    public double? EvaluationTimeMs { get; init; }

    /// <summary>PEP identifier.</summary>
    public string? PepId { get; init; }

    /// <summary>Actor identity for PAP events.</summary>
    public string? ActorIdentity { get; init; }

    /// <summary>Additional detail for PAP events (JSON).</summary>
    public string? DetailJson { get; init; }

    /// <summary>Tenant identifier for multi-tenant isolation. Null = default/system tenant.</summary>
    public string? TenantId { get; init; }
}

/// <summary>
/// Query parameters for audit event retrieval.
/// </summary>
public sealed record AuditQuery
{
    /// <summary>Start of the time range filter (inclusive).</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>End of the time range filter (inclusive).</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Filter by subject identifier.</summary>
    public string? SubjectId { get; init; }

    /// <summary>Filter by resource identifier.</summary>
    public string? ResourceId { get; init; }

    /// <summary>Filter by event type.</summary>
    public string? EventType { get; init; }

    /// <summary>Filter by decision outcome.</summary>
    public string? Decision { get; init; }

    /// <summary>Maximum number of events per page.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>One-based page number.</summary>
    public int Page { get; init; } = 1;
}

/// <summary>
/// Paged result of audit events.
/// </summary>
public sealed record AuditQueryResult
{
    /// <summary>Audit events on this page.</summary>
    public required List<AuditEvent> Events { get; init; }

    /// <summary>Total number of matching events across all pages.</summary>
    public required int TotalCount { get; init; }

    /// <summary>Current page number (one-based).</summary>
    public required int Page { get; init; }

    /// <summary>Page size used for this query.</summary>
    public required int PageSize { get; init; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
