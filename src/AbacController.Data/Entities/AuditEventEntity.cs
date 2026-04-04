namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for audit events. Append-only.
/// </summary>
public class AuditEventEntity
{
    /// <summary>Unique audit event identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Event timestamp (UTC).</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Event type: "evaluation", "policy-change", "spif-import", etc.</summary>
    public string EventType { get; set; } = "";

    /// <summary>Caller-provided request correlation ID.</summary>
    public string? RequestId { get; set; }

    /// <summary>Generated decision ID for evaluation events.</summary>
    public string? DecisionId { get; set; }

    /// <summary>Subject type (e.g., "user", "service").</summary>
    public string? SubjectType { get; set; }

    /// <summary>Subject identifier.</summary>
    public string? SubjectId { get; set; }

    /// <summary>Requested action name.</summary>
    public string? ActionName { get; set; }

    /// <summary>Resource type.</summary>
    public string? ResourceType { get; set; }

    /// <summary>Resource identifier.</summary>
    public string? ResourceId { get; set; }

    /// <summary>XACML decision outcome (Permit, Deny, NotApplicable, Indeterminate).</summary>
    public string? Decision { get; set; }

    /// <summary>JSON array of applied policy IDs.</summary>
    public string? AppliedPolicies { get; set; }

    /// <summary>JSON serialized obligations.</summary>
    public string? ObligationsJson { get; set; }

    /// <summary>JSON serialized attribute provenance information.</summary>
    public string? AttributesUsedJson { get; set; }

    /// <summary>Evaluation duration in milliseconds.</summary>
    public double? EvaluationTimeMs { get; set; }

    /// <summary>PEP identifier that initiated the request.</summary>
    public string? PepId { get; set; }

    /// <summary>Authenticated actor identity.</summary>
    public string? ActorIdentity { get; set; }

    /// <summary>Additional event details as JSON.</summary>
    public string? DetailJson { get; set; }

    /// <summary>Tenant identifier for multi-tenant isolation. Null = default/system tenant.</summary>
    public string? TenantId { get; set; }
}
