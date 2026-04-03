using System.Collections.Immutable;
using AbacController.Core.Domain.Spif;

namespace AbacController.Core.Domain.Labels;

/// <summary>
/// A security label attached to a resource. Contains classification and category values
/// that reference a specific SPIF via policy OID.
/// </summary>
public sealed record SecurityLabel
{
    /// <summary>Policy OID this label is governed by.</summary>
    public string? PolicyOid { get; init; }

    /// <summary>Policy name (human-readable, from label decode).</summary>
    public string? PolicyName { get; init; }

    /// <summary>Classification LACV value.</summary>
    public LacvValue ClassificationLacv { get; init; }

    /// <summary>Classification name (human-readable, from label decode).</summary>
    public string? ClassificationName { get; init; }

    /// <summary>Category tag sets with their values.</summary>
    public ImmutableList<LabelCategoryTagSet> CategoryTagSets { get; init; }
        = ImmutableList<LabelCategoryTagSet>.Empty;

    /// <summary>Privacy marks (caveats).</summary>
    public ImmutableList<string> PrivacyMarks { get; init; }
        = ImmutableList<string>.Empty;

    /// <summary>Creation timestamp of the label.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>
/// Category tag set values within a security label.
/// </summary>
public sealed record LabelCategoryTagSet
{
    /// <summary>Tag set OID.</summary>
    public required string TagSetOid { get; init; }

    /// <summary>Tags with their values.</summary>
    public ImmutableList<LabelCategoryTag> Tags { get; init; }
        = ImmutableList<LabelCategoryTag>.Empty;
}

/// <summary>
/// A category tag within a label — carries the bit set or enumerated values.
/// </summary>
public sealed record LabelCategoryTag
{
    /// <summary>Tag name.</summary>
    public string? Name { get; init; }

    /// <summary>Tag type for ACDF evaluation.</summary>
    public required TagType TagType { get; init; }

    /// <summary>Enum type if tag type is enumerated.</summary>
    public EnumType? EnumType { get; init; }

    /// <summary>Tag OID (for lookup within tag set).</summary>
    public string? TagOid { get; init; }

    /// <summary>Bit set for restrictive/permissive tags.</summary>
    public ImmutableHashSet<LacvValue> Bits { get; init; }
        = ImmutableHashSet<LacvValue>.Empty;

    /// <summary>Enumerated values for enumerated tags.</summary>
    public ImmutableHashSet<LacvValue> EnumeratedValues { get; init; }
        = ImmutableHashSet<LacvValue>.Empty;

    /// <summary>Individual category entries (with validity info).</summary>
    public ImmutableList<LabelCategory> Categories { get; init; }
        = ImmutableList<LabelCategory>.Empty;
}

/// <summary>
/// An individual category value within a label tag.
/// </summary>
public sealed record LabelCategory
{
    /// <summary>Category name.</summary>
    public required string Name { get; init; }

    /// <summary>Category LACV.</summary>
    public required LacvValue Lacv { get; init; }

    /// <summary>Validity start.</summary>
    public DateTimeOffset? NotBefore { get; init; }

    /// <summary>Validity end.</summary>
    public DateTimeOffset? NotAfter { get; init; }
}
