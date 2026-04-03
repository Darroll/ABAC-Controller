namespace AbacController.Core.Domain.Spif;

/// <summary>
/// SPIF security category tag types. Determines ACDF evaluation semantics.
/// </summary>
public enum TagType
{
    /// <summary>AND group — all label bits must be present in clearance.</summary>
    Restrictive,

    /// <summary>OR group — at least one label bit must be present in clearance.</summary>
    Permissive,

    /// <summary>Integer set, sub-typed by <see cref="EnumType"/>.</summary>
    Enumerated,

    /// <summary>Informative/caveats — NOT checked in ACDF.</summary>
    TagType7,

    /// <summary>Structural only — not used in ACDF.</summary>
    NotApplicable
}

/// <summary>
/// Sub-type for enumerated category tags.
/// </summary>
public enum EnumType
{
    /// <summary>All enumerated values in label must be in clearance.</summary>
    Restrictive,

    /// <summary>At least one enumerated value in label must be in clearance.</summary>
    Permissive
}
