using System.Collections.Immutable;
using AbacController.Core.Domain.Spif;

namespace AbacController.Core.Domain.Classifications;

/// <summary>
/// Result of an allowed-classifications query. Contains the filtered set of
/// classifications the subject may assign in the given context.
/// </summary>
public sealed record AllowedClassificationsResult
{
    /// <summary>Correlated request ID.</summary>
    public string? RequestId { get; init; }

    /// <summary>Unique result identifier.</summary>
    public string ResultId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Governing SPIF policy OID.</summary>
    public required string PolicyOid { get; init; }

    /// <summary>Governing SPIF policy name.</summary>
    public string? PolicyName { get; init; }

    /// <summary>Application scope used (if any).</summary>
    public string? ApplicationId { get; init; }

    /// <summary>Tenant context.</summary>
    public string? TenantId { get; init; }

    /// <summary>Allowed classifications after three-layer filtering.</summary>
    public required List<AllowedClassification> Classifications { get; init; }

    /// <summary>Total classification count in the SPIF before filtering.</summary>
    public int TotalSpifClassifications { get; init; }

    /// <summary>Evaluation duration.</summary>
    public TimeSpan EvaluationTime { get; init; }

    /// <summary>Optional diagnostic filter trace.</summary>
    public ClassificationFilterTrace? Trace { get; init; }
}

/// <summary>
/// A single classification that the subject is permitted to assign.
/// </summary>
public sealed record AllowedClassification
{
    /// <summary>Classification name (e.g., "SECRET").</summary>
    public required string Name { get; init; }

    /// <summary>Label and Certificate Value.</summary>
    public required int Lacv { get; init; }

    /// <summary>Hierarchy value for ordering.</summary>
    public required int Hierarchy { get; init; }

    /// <summary>Foreground color from SPIF.</summary>
    public string? FgColor { get; init; }

    /// <summary>Background color from SPIF.</summary>
    public string? BgColor { get; init; }

    /// <summary>Marking data from SPIF (when requested).</summary>
    public ImmutableList<MarkingData>? MarkingData { get; init; }

    /// <summary>Allowed category tag sets for this classification (when requested).</summary>
    public List<AllowedCategoryTagSet>? AllowedCategories { get; init; }

    /// <summary>Required category constraints from the SPIF classification.</summary>
    public ImmutableList<RequiredCategoryConstraint> RequiredCategories { get; init; }
        = ImmutableList<RequiredCategoryConstraint>.Empty;

    /// <summary>Excluded category constraints from the SPIF classification.</summary>
    public ImmutableList<ExcludedCategoryRef> ExcludedCategories { get; init; }
        = ImmutableList<ExcludedCategoryRef>.Empty;
}

/// <summary>
/// An allowed category tag set within a classification.
/// </summary>
public sealed record AllowedCategoryTagSet
{
    /// <summary>Tag set OID.</summary>
    public required string TagSetOid { get; init; }

    /// <summary>Tag set name.</summary>
    public required string Name { get; init; }

    /// <summary>Allowed tags within this set.</summary>
    public required List<AllowedCategoryTag> Tags { get; init; }
}

/// <summary>
/// An allowed tag within a category tag set.
/// </summary>
public sealed record AllowedCategoryTag
{
    /// <summary>Tag name.</summary>
    public required string Name { get; init; }

    /// <summary>Tag OID.</summary>
    public string? TagOid { get; init; }

    /// <summary>Tag type (Restrictive, Permissive, Enumerated).</summary>
    public required TagType TagType { get; init; }

    /// <summary>Allowed categories within this tag.</summary>
    public required List<AllowedCategory> Categories { get; init; }
}

/// <summary>
/// An allowed category value within a tag.
/// </summary>
public sealed record AllowedCategory
{
    /// <summary>Category name.</summary>
    public required string Name { get; init; }

    /// <summary>Category LACV value.</summary>
    public required int Lacv { get; init; }
}

/// <summary>
/// Diagnostic trace of three-layer classification filtering.
/// </summary>
public sealed record ClassificationFilterTrace
{
    /// <summary>Individual filter steps.</summary>
    public required List<ClassificationFilterStep> Steps { get; init; }
}

/// <summary>
/// A single classification evaluated against a filter layer.
/// </summary>
public sealed record ClassificationFilterStep
{
    /// <summary>Classification name.</summary>
    public required string ClassificationName { get; init; }

    /// <summary>Classification LACV.</summary>
    public required int Lacv { get; init; }

    /// <summary>Filter layer that produced this step.</summary>
    public required string FilterLayer { get; init; }

    /// <summary>Whether the classification passed this layer.</summary>
    public required bool Passed { get; init; }

    /// <summary>Reason for the decision.</summary>
    public string? Reason { get; init; }
}
