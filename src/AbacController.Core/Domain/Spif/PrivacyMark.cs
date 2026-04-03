using System.Collections.Immutable;

namespace AbacController.Core.Domain.Spif;

/// <summary>
/// Privacy marks container — human-readable caveats NOT used in ACDF.
/// </summary>
public sealed record PrivacyMarks
{
    /// <summary>Maximum number of privacy marks that can be selected simultaneously.</summary>
    public int? MaxSelection { get; init; }

    /// <summary>Minimum number of privacy marks required.</summary>
    public int? MinSelection { get; init; }

    /// <summary>Available privacy marks.</summary>
    public ImmutableList<PrivacyMark> Marks { get; init; } = ImmutableList<PrivacyMark>.Empty;
}

/// <summary>
/// A single privacy mark definition.
/// </summary>
public sealed record PrivacyMark
{
    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>If true, cannot be used in newly created labels.</summary>
    public bool Obsolete { get; init; }

    /// <summary>Marking data for this privacy mark.</summary>
    public ImmutableList<MarkingData> MarkingData { get; init; } = ImmutableList<MarkingData>.Empty;
}
