using System.Collections.Immutable;
using AbacController.Core.Domain.Spif;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Pre-compiled, immutable index of a SPIF for fast ACDF evaluation.
/// Built at SPIF load time; shared read-only across all evaluation threads.
/// </summary>
public interface ISpifIndex
{
    /// <summary>Policy OID for this SPIF.</summary>
    string PolicyOid { get; }

    /// <summary>Human-readable policy name.</summary>
    string PolicyName { get; }

    /// <summary>Schema version.</summary>
    string SchemaVersion { get; }

    /// <summary>Get hierarchy value for a classification LACV. Returns false if not found.</summary>
    bool TryGetHierarchy(LacvValue lacv, out int hierarchy);

    /// <summary>Get classification info by LACV.</summary>
    SecurityClassification? GetClassification(LacvValue lacv);

    /// <summary>Get classification name by LACV.</summary>
    string? GetClassificationName(LacvValue lacv);

    /// <summary>Get a tag set index by OID.</summary>
    ITagSetIndex? GetTagSet(string tagSetOid);

    /// <summary>Get equivalent policy mapping.</summary>
    EquivalentPolicy? GetEquivalentPolicy(string targetPolicyOid);

    /// <summary>Get all classification LACV values ordered by hierarchy.</summary>
    IReadOnlyList<LacvValue> GetClassificationsByHierarchy();

    /// <summary>Get all tag set OIDs.</summary>
    IReadOnlyList<string> GetTagSetOids();

    /// <summary>Get the underlying SPIF domain model.</summary>
    Spif Spif { get; }
}

/// <summary>
/// Index for a single tag set within a SPIF.
/// </summary>
public interface ITagSetIndex
{
    /// <summary>Tag set OID.</summary>
    string TagSetOid { get; }

    /// <summary>Tag set name.</summary>
    string Name { get; }

    /// <summary>Get all tags in this set.</summary>
    IReadOnlyList<SecurityCategoryTag> Tags { get; }

    /// <summary>Get a category by LACV within a specific tag.</summary>
    TagCategory? GetCategory(string tagName, LacvValue lacv);

    /// <summary>Get all category LACVs for a tag.</summary>
    ImmutableHashSet<LacvValue> GetCategoryLacvs(string tagName);
}
