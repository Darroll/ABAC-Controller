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
}

/// <summary>
/// Query parameters for audit event retrieval.
/// </summary>
public sealed record AuditQuery
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? SubjectId { get; init; }
    public string? ResourceId { get; init; }
    public string? EventType { get; init; }
    public string? Decision { get; init; }
    public int PageSize { get; init; } = 50;
    public int Page { get; init; } = 1;
}

/// <summary>
/// Paged result of audit events.
/// </summary>
public sealed record AuditQueryResult
{
    public required List<AuditEvent> Events { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
