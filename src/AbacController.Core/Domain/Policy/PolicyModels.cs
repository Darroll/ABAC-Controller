namespace AbacController.Core.Domain.Policy;

/// <summary>
/// A policy set — groups related policies with a combining algorithm.
/// </summary>
public sealed record PolicySet
{
    /// <summary>Policy set identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Description.</summary>
    public string? Description { get; init; }

    /// <summary>Associated SPIF ID (optional).</summary>
    public Guid? SpifId { get; init; }

    /// <summary>Combining algorithm (e.g., "deny-overrides", "permit-overrides").</summary>
    public string CombiningAlgorithm { get; init; } = "deny-overrides";

    /// <summary>Whether this policy set is active.</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Policies in this set.</summary>
    public List<Policy> Policies { get; init; } = [];
}

/// <summary>
/// A policy within a policy set.
/// </summary>
public sealed record Policy
{
    /// <summary>Policy identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Parent policy set ID.</summary>
    public required string PolicySetId { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Policy format: "xacml-json", "xacml-xml", or "native".</summary>
    public string Format { get; init; } = "native";

    /// <summary>Active version ID.</summary>
    public Guid? ActiveVersionId { get; init; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Policy versions.</summary>
    public List<PolicyVersion> Versions { get; init; } = [];
}

/// <summary>
/// An immutable snapshot of a policy at a point in time.
/// </summary>
public sealed record PolicyVersion
{
    /// <summary>Version identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Parent policy ID.</summary>
    public required string PolicyId { get; init; }

    /// <summary>Auto-incrementing version number per policy.</summary>
    public required int VersionNumber { get; init; }

    /// <summary>Policy content (JSON/XML body).</summary>
    public required string Content { get; init; }

    /// <summary>SHA256 hash of content.</summary>
    public required string Hash { get; init; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Identity of who created this version.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Whether this is the active version.</summary>
    public bool IsActive { get; init; }
}
