namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for a policy set.
/// </summary>
public class PolicySetEntity
{
    /// <summary>Unique policy set identifier.</summary>
    public string Id { get; set; } = "";

    /// <summary>Human-readable name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Associated SPIF entity ID.</summary>
    public Guid? SpifId { get; set; }

    /// <summary>XACML combining algorithm (e.g., "deny-overrides").</summary>
    public string CombiningAlgorithm { get; set; } = "deny-overrides";

    /// <summary>Whether this policy set is active for evaluation.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last update timestamp (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Tenant identifier for multi-tenant isolation. Null = default/system tenant.</summary>
    public string? TenantId { get; set; }

    /// <summary>Navigation: associated SPIF entity.</summary>
    public SpifEntity? Spif { get; set; }

    /// <summary>Navigation: policies within this set.</summary>
    public List<PolicyEntity> Policies { get; set; } = [];
}

/// <summary>
/// EF Core entity for a policy.
/// </summary>
public class PolicyEntity
{
    /// <summary>Unique policy identifier.</summary>
    public string Id { get; set; } = "";

    /// <summary>Parent policy set identifier.</summary>
    public string PolicySetId { get; set; } = "";

    /// <summary>Human-readable name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Policy format: "native", "xacml-json", or "xacml-xml".</summary>
    public string Format { get; set; } = "native";

    /// <summary>ID of the currently active version.</summary>
    public Guid? ActiveVersionId { get; set; }

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last update timestamp (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Navigation: parent policy set.</summary>
    public PolicySetEntity? PolicySet { get; set; }

    /// <summary>Navigation: all versions of this policy.</summary>
    public List<PolicyVersionEntity> Versions { get; set; } = [];
}

/// <summary>
/// EF Core entity for a policy version (immutable snapshot).
/// </summary>
public class PolicyVersionEntity
{
    /// <summary>Unique version identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Parent policy identifier.</summary>
    public string PolicyId { get; set; } = "";

    /// <summary>Auto-incrementing version number.</summary>
    public int VersionNumber { get; set; }

    /// <summary>Policy content (JSON or XML body).</summary>
    public string Content { get; set; } = "";

    /// <summary>SHA256 hash of content for change detection.</summary>
    public string Hash { get; set; } = "";

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Identity of the user who created this version.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Whether this is the currently active version.</summary>
    public bool IsActive { get; set; }

    /// <summary>Navigation: parent policy.</summary>
    public PolicyEntity? Policy { get; set; }
}
