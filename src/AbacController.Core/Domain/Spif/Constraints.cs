using System.Collections.Immutable;

namespace AbacController.Core.Domain.Spif;

/// <summary>
/// A required category constraint — specifies categories that MUST be present.
/// </summary>
public sealed record RequiredCategoryConstraint
{
    /// <summary>
    /// Operation: "oneOrMore" means at least one category group must be present;
    /// "all" means all must be present.
    /// </summary>
    public required string Operation { get; init; }

    /// <summary>Category groups that satisfy this constraint.</summary>
    public ImmutableList<CategoryGroupRef> CategoryGroups { get; init; }
        = ImmutableList<CategoryGroupRef>.Empty;
}

/// <summary>
/// Reference to a category group within a constraint.
/// </summary>
public sealed record CategoryGroupRef
{
    /// <summary>Name of the referenced tag set.</summary>
    public required string TagSetRef { get; init; }

    /// <summary>Tag type of the referenced category.</summary>
    public required TagType TagType { get; init; }

    /// <summary>LACV of the referenced category.</summary>
    public required LacvValue Lacv { get; init; }

    /// <summary>Enum type if tag type is enumerated.</summary>
    public EnumType? EnumType { get; init; }
}

/// <summary>
/// Reference to an excluded category.
/// </summary>
public sealed record ExcludedCategoryRef
{
    /// <summary>Name of the referenced tag set.</summary>
    public required string TagSetRef { get; init; }

    /// <summary>Tag type of the excluded category.</summary>
    public required TagType TagType { get; init; }

    /// <summary>LACV of the excluded category.</summary>
    public required LacvValue Lacv { get; init; }
}
