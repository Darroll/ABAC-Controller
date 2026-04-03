using System.Collections.Immutable;

namespace AbacController.Core.Domain.Spif;

/// <summary>
/// Root domain model for a Security Policy Information File (SPIF).
/// Contains the complete machine-readable representation of a security labelling policy.
/// </summary>
public sealed record Spif
{
    /// <summary>Schema version: "1.0", "2.0", "2.1", or "3.0".</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>Policy version (internal counter).</summary>
    public string? Version { get; init; }

    /// <summary>Creation date of the SPIF.</summary>
    public DateTimeOffset? CreationDate { get; init; }

    /// <summary>DN of the entity that created the SPIF.</summary>
    public string? OriginatorDn { get; init; }

    /// <summary>Key used to sign the SPIF.</summary>
    public string? KeyIdentifier { get; init; }

    /// <summary>OID of the privilege in which this SPIF is referenced.</summary>
    public string? PrivilegeId { get; init; }

    /// <summary>OID for role-based access control context.</summary>
    public string? RbacId { get; init; }

    /// <summary>Policy identifier (name and OID).</summary>
    public required PolicyInfo PolicyId { get; init; }

    /// <summary>All security classifications in this policy.</summary>
    public ImmutableList<SecurityClassification> Classifications { get; init; }
        = ImmutableList<SecurityClassification>.Empty;

    /// <summary>All security category tag sets.</summary>
    public ImmutableList<SecurityCategoryTagSet> CategoryTagSets { get; init; }
        = ImmutableList<SecurityCategoryTagSet>.Empty;

    /// <summary>Equivalent policy declarations for cross-domain mapping.</summary>
    public ImmutableList<EquivalentPolicy> EquivalentPolicies { get; init; }
        = ImmutableList<EquivalentPolicy>.Empty;

    /// <summary>Privacy marks (caveats not used in ACDF).</summary>
    public PrivacyMarks? PrivacyMarks { get; init; }

    /// <summary>Global marking data.</summary>
    public ImmutableList<MarkingData> GlobalMarkingData { get; init; }
        = ImmutableList<MarkingData>.Empty;

    /// <summary>Global marking qualifiers.</summary>
    public ImmutableList<MarkingQualifier> GlobalMarkingQualifiers { get; init; }
        = ImmutableList<MarkingQualifier>.Empty;
}

/// <summary>
/// Uniquely identifies a security policy.
/// </summary>
public sealed record PolicyInfo
{
    /// <summary>Human-readable policy name.</summary>
    public required string Name { get; init; }

    /// <summary>OID uniquely identifying the policy globally.</summary>
    public required string Oid { get; init; }

    /// <summary>Marking data for the policy name.</summary>
    public ImmutableList<MarkingData> MarkingData { get; init; }
        = ImmutableList<MarkingData>.Empty;
}
