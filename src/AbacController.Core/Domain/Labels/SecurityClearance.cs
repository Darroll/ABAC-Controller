using System.Collections.Immutable;
using AbacController.Core.Domain.Spif;

namespace AbacController.Core.Domain.Labels;

/// <summary>
/// A security clearance held by a subject. Contains a set of classification values
/// and category values against which security labels are evaluated.
/// </summary>
public sealed record SecurityClearance
{
    /// <summary>Policy OID this clearance references.</summary>
    public required string PolicyOid { get; init; }

    /// <summary>
    /// Set of classification LACV values the holder is cleared for.
    /// Clearance holds a set (not just one) to support non-linear hierarchies.
    /// </summary>
    public ImmutableHashSet<LacvValue> ClassificationLacvs { get; init; }
        = ImmutableHashSet<LacvValue>.Empty;

    /// <summary>Category tag sets with their values.</summary>
    public ImmutableList<ClearanceCategoryTagSet> CategoryTagSets { get; init; }
        = ImmutableList<ClearanceCategoryTagSet>.Empty;

    /// <summary>
    /// Get a category tag set by OID.
    /// </summary>
    public ClearanceCategoryTagSet? GetTagSet(string tagSetOid)
        => CategoryTagSets.FirstOrDefault(ts => ts.TagSetOid == tagSetOid);
}

/// <summary>
/// Category tag set values within a clearance.
/// </summary>
public sealed record ClearanceCategoryTagSet
{
    /// <summary>Tag set OID.</summary>
    public required string TagSetOid { get; init; }

    /// <summary>Tags with their granted values.</summary>
    public ImmutableList<ClearanceCategoryTag> Tags { get; init; }
        = ImmutableList<ClearanceCategoryTag>.Empty;

    /// <summary>
    /// Get bit set for a tag identified by tag type key.
    /// </summary>
    public ImmutableHashSet<LacvValue>? GetBits(string? tagOid)
        => Tags.FirstOrDefault(t => t.TagOid == tagOid)?.Bits;

    /// <summary>
    /// Get enumerated values for a tag identified by tag type key.
    /// </summary>
    public ImmutableHashSet<LacvValue>? GetEnumeratedValues(string? tagOid)
        => Tags.FirstOrDefault(t => t.TagOid == tagOid)?.EnumeratedValues;
}

/// <summary>
/// A category tag within a clearance.
/// </summary>
public sealed record ClearanceCategoryTag
{
    /// <summary>Tag OID (for lookup within tag set).</summary>
    public string? TagOid { get; init; }

    /// <summary>Tag type.</summary>
    public required TagType TagType { get; init; }

    /// <summary>Bit set for restrictive/permissive tags.</summary>
    public ImmutableHashSet<LacvValue> Bits { get; init; }
        = ImmutableHashSet<LacvValue>.Empty;

    /// <summary>Enumerated values for enumerated tags.</summary>
    public ImmutableHashSet<LacvValue> EnumeratedValues { get; init; }
        = ImmutableHashSet<LacvValue>.Empty;
}
