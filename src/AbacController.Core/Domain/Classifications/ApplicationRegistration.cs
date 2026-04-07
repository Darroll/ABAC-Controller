namespace AbacController.Core.Domain.Classifications;

/// <summary>
/// A registered application with classification scope constraints.
/// Applications define which subset of SPIF classifications are permitted
/// for a particular integration (e.g., email, document management, messaging).
/// </summary>
public sealed record ApplicationRegistration
{
    /// <summary>Unique application identifier (e.g., "email-classification").</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable application name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Default SPIF policy OID for this application.
    /// Falls back to tenant default when null.
    /// </summary>
    public string? DefaultPolicyOid { get; init; }

    /// <summary>
    /// Whitelist of classification LACV values this application may use.
    /// Empty list means all classifications are allowed (subject to clearance and policy).
    /// </summary>
    public List<int> AllowedClassificationLacvs { get; init; } = [];

    /// <summary>
    /// Maximum classification hierarchy value this application may use.
    /// Null means no ceiling.
    /// </summary>
    public int? MaxClassificationHierarchy { get; init; }

    /// <summary>
    /// Whitelist of category tag set OIDs this application may use.
    /// Empty list means all tag sets are allowed.
    /// </summary>
    public List<string> AllowedTagSetOids { get; init; } = [];

    /// <summary>Whether this application registration is active.</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>Tenant scope (null for system-wide).</summary>
    public string? TenantId { get; init; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
