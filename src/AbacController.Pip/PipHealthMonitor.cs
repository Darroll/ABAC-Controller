using System.Collections.Concurrent;
using AbacController.Core.Interfaces;

namespace AbacController.Pip;

/// <summary>
/// Monitors PIP source health by periodically testing connectivity.
/// Tracks availability status and response times for dashboard reporting.
/// </summary>
public sealed class PipHealthMonitor
{
    private readonly IEnumerable<IPipSource> _sources;
    private readonly ConcurrentDictionary<string, PipSourceStatus> _statuses = new(StringComparer.Ordinal);

    public PipHealthMonitor(IEnumerable<IPipSource> sources)
    {
        _sources = sources;
    }

    /// <summary>
    /// Check health of all registered PIP sources.
    /// </summary>
    public async Task<IReadOnlyList<PipSourceStatus>> CheckAllAsync(CancellationToken ct = default)
    {
        var tasks = _sources.Select(async source =>
        {
            var status = await CheckSourceAsync(source, ct);
            _statuses[source.SourceId] = status;
            return status;
        });

        return (await Task.WhenAll(tasks)).ToList();
    }

    /// <summary>
    /// Get the last known status of all sources.
    /// </summary>
    public IReadOnlyList<PipSourceStatus> GetStatuses()
        => _statuses.Values.OrderBy(static s => s.SourceId, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Get health status for a specific source.
    /// </summary>
    public PipSourceStatus? GetStatus(string sourceId)
        => _statuses.TryGetValue(sourceId, out var status) ? status : null;

    private static async Task<PipSourceStatus> CheckSourceAsync(IPipSource source, CancellationToken ct)
    {
        try
        {
            var healthResult = await source.TestConnectivityAsync(ct);
            return new PipSourceStatus
            {
                SourceId = source.SourceId,
                SourceType = source.SourceType,
                Healthy = healthResult.Healthy,
                Message = healthResult.Message,
                ResponseTime = healthResult.ResponseTime,
                LastChecked = DateTimeOffset.UtcNow,
                ProvidesAttributes = source.ProvidesAttributes.ToList()
            };
        }
        catch (Exception ex)
        {
            return new PipSourceStatus
            {
                SourceId = source.SourceId,
                SourceType = source.SourceType,
                Healthy = false,
                Message = $"Health check failed: {ex.Message}",
                LastChecked = DateTimeOffset.UtcNow,
                ProvidesAttributes = source.ProvidesAttributes.ToList()
            };
        }
    }
}

/// <summary>
/// Health status of a PIP source.
/// </summary>
public sealed record PipSourceStatus
{
    public required string SourceId { get; init; }
    public required string SourceType { get; init; }
    public required bool Healthy { get; init; }
    public string? Message { get; init; }
    public TimeSpan? ResponseTime { get; init; }
    public DateTimeOffset LastChecked { get; init; }
    public List<string> ProvidesAttributes { get; init; } = [];
}
