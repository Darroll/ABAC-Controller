using System.Collections.Immutable;

namespace AbacController.Core.Domain.Spif;

/// <summary>
/// A security category tag set — groups related categories by OID.
/// Maps to SDN.801c "tag set".
/// </summary>
public sealed record SecurityCategoryTagSet
{
    /// <summary>Unique OID identifying this tag set.</summary>
    public required string TagSetOid { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Category tags within this tag set.</summary>
    public ImmutableList<SecurityCategoryTag> Tags { get; init; }
        = ImmutableList<SecurityCategoryTag>.Empty;
}

/// <summary>
/// A security category tag — a group of category values with shared semantics.
/// </summary>
public sealed record SecurityCategoryTag
{
    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Tag type determines ACDF evaluation semantics.</summary>
    public required TagType TagType { get; init; }

    /// <summary>Sub-type for enumerated tags.</summary>
    public EnumType? EnumType { get; init; }

    /// <summary>Individual category values within this tag.</summary>
    public ImmutableList<TagCategory> Categories { get; init; }
        = ImmutableList<TagCategory>.Empty;

    /// <summary>Marking data for this tag group.</summary>
    public ImmutableList<MarkingData> MarkingData { get; init; }
        = ImmutableList<MarkingData>.Empty;
}

/// <summary>
/// An individual category value within a tag group.
/// </summary>
public sealed record TagCategory
{
    /// <summary>Human-readable name (e.g., "ATOMAL", "USA").</summary>
    public required string Name { get; init; }

    /// <summary>Label and Certificate Value for this category.</summary>
    public required LacvValue Lacv { get; init; }

    /// <summary>If true, cannot be used in newly created labels.</summary>
    public bool Obsolete { get; init; }

    /// <summary>Classification required when this category is selected.</summary>
    public string? RequiredClass { get; init; }

    /// <summary>User input type: "string", "integer", "date", or null.</summary>
    public string? UserInput { get; init; }

    /// <summary>Date format string if UserInput is "date".</summary>
    public string? DateFormat { get; init; }

    /// <summary>Validity start (category not valid before this time).</summary>
    public DateTimeOffset? NotBefore { get; init; }

    /// <summary>Validity end (category not valid after this time).</summary>
    public DateTimeOffset? NotAfter { get; init; }

    /// <summary>Marking data for this category value.</summary>
    public ImmutableList<MarkingData> MarkingData { get; init; }
        = ImmutableList<MarkingData>.Empty;

    /// <summary>Equivalent category in another policy.</summary>
    public ImmutableList<EquivalentCategoryTag> EquivalentCategories { get; init; }
        = ImmutableList<EquivalentCategoryTag>.Empty;

    /// <summary>Classifications excluded when this category is selected.</summary>
    public ImmutableList<string> ExcludedClasses { get; init; }
        = ImmutableList<string>.Empty;

    /// <summary>Categories required when this category is selected.</summary>
    public ImmutableList<RequiredCategoryConstraint> RequiredCategories { get; init; }
        = ImmutableList<RequiredCategoryConstraint>.Empty;

    /// <summary>Categories excluded when this category is selected.</summary>
    public ImmutableList<ExcludedCategoryRef> ExcludedCategories { get; init; }
        = ImmutableList<ExcludedCategoryRef>.Empty;
}

/// <summary>
/// Equivalent category mapping to another policy.
/// </summary>
public sealed record EquivalentCategoryTag
{
    /// <summary>OID of the equivalent policy.</summary>
    public required string PolicyRef { get; init; }

    /// <summary>Tag set identifier in the equivalent policy.</summary>
    public required string TagSetId { get; init; }

    /// <summary>Tag type in the equivalent policy.</summary>
    public required TagType TagType { get; init; }

    /// <summary>LACV in the equivalent policy.</summary>
    public required LacvValue Lacv { get; init; }

    /// <summary>Direction of the equivalency mapping.</summary>
    public required EquivalencyDirection Applied { get; init; }

    /// <summary>Optional action qualifier for the equivalence.</summary>
    public string? Action { get; init; }
}
