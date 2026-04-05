namespace AbacController.Core.Interfaces;

/// <summary>
/// Provides the currently configured runtime PIP sources.
/// </summary>
public interface IPipSourceCatalog
{
    /// <summary>
    /// Loads the current set of PIP sources available to the resolver and health checks.
    /// </summary>
    Task<IReadOnlyList<IPipSource>> GetSourcesAsync(CancellationToken ct = default);
}
