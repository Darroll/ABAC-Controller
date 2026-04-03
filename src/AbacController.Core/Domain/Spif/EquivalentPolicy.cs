namespace AbacController.Core.Domain.Spif;

/// <summary>
/// Declares a policy to which this SPIF's policy maps, enabling cross-domain label translation.
/// </summary>
public sealed record EquivalentPolicy
{
    /// <summary>Human-readable name of the equivalent policy.</summary>
    public required string Name { get; init; }

    /// <summary>OID of the equivalent policy.</summary>
    public required string PolicyOid { get; init; }

    /// <summary>Optional URI to the equivalent SPIF document.</summary>
    public string? DocRefUri { get; init; }
}
