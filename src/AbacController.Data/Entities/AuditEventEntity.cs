namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for audit events. Append-only.
/// </summary>
public class AuditEventEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string EventType { get; set; } = "";
    public string? RequestId { get; set; }
    public string? DecisionId { get; set; }
    public string? SubjectType { get; set; }
    public string? SubjectId { get; set; }
    public string? ActionName { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? Decision { get; set; }
    public string? AppliedPolicies { get; set; }
    public string? ObligationsJson { get; set; }
    public string? AttributesUsedJson { get; set; }
    public double? EvaluationTimeMs { get; set; }
    public string? PepId { get; set; }
    public string? ActorIdentity { get; set; }
    public string? DetailJson { get; set; }
}
