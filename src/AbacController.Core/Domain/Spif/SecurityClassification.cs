using System.Collections.Immutable;

namespace AbacController.Core.Domain.Spif;

/// <summary>
/// A security classification level within a SPIF. Defines one hierarchical
/// sensitivity level (e.g., UNCLASSIFIED, CONFIDENTIAL, SECRET, TOP SECRET).
/// </summary>
public sealed record SecurityClassification
{
    /// <summary>Human-readable name (e.g., "NATO SECRET").</summary>
    public required string Name { get; init; }

    /// <summary>Label and Certificate Value — wire value in security labels.</summary>
    public required LacvValue Lacv { get; init; }

    /// <summary>
    /// Hierarchy value for dominance comparison. Higher = more sensitive.
    /// Note: hierarchy and lacv can differ (e.g., GENSER policy).
    /// </summary>
    public required int Hierarchy { get; init; }

    /// <summary>If true, cannot be used in newly created labels.</summary>
    public bool Obsolete { get; init; }

    /// <summary>Visual rendering color (v2.1 single color).</summary>
    public string? Color { get; init; }

    /// <summary>Foreground color (v3.0).</summary>
    public string? FgColor { get; init; }

    /// <summary>Background color (v3.0).</summary>
    public string? BgColor { get; init; }

    /// <summary>Marking data for this classification.</summary>
    public ImmutableList<MarkingData> MarkingData { get; init; } = ImmutableList<MarkingData>.Empty;

    /// <summary>Equivalent classifications in other policies.</summary>
    public ImmutableList<EquivalentClassification> EquivalentClassifications { get; init; }
        = ImmutableList<EquivalentClassification>.Empty;

    /// <summary>Categories required when this classification is selected.</summary>
    public ImmutableList<RequiredCategoryConstraint> RequiredCategories { get; init; }
        = ImmutableList<RequiredCategoryConstraint>.Empty;

    /// <summary>Categories excluded when this classification is selected.</summary>
    public ImmutableList<ExcludedCategoryRef> ExcludedCategories { get; init; }
        = ImmutableList<ExcludedCategoryRef>.Empty;
}

/// <summary>
/// Equivalent classification mapping to another policy.
/// </summary>
public sealed record EquivalentClassification
{
    /// <summary>Name of the referenced policy.</summary>
    public required string PolicyRef { get; init; }

    /// <summary>LACV in the target policy.</summary>
    public required LacvValue Lacv { get; init; }

    /// <summary>When to apply: encrypt, decrypt, or both.</summary>
    public required EquivalencyDirection Applied { get; init; }

    /// <summary>Required categories to add during equivalency mapping.</summary>
    public ImmutableList<RequiredCategoryConstraint> RequiredCategories { get; init; }
        = ImmutableList<RequiredCategoryConstraint>.Empty;
}

/// <summary>
/// Direction for equivalency application.
/// </summary>
public enum EquivalencyDirection
{
    Encrypt,
    Decrypt,
    Both
}
