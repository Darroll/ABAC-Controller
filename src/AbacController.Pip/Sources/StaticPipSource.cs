using AbacController.Core.Domain.Attributes;
using AbacController.Core.Interfaces;

namespace AbacController.Pip.Sources;

/// <summary>
/// Static/test attribute source. Returns hard-coded attribute values from configuration.
/// For development and testing only.
/// </summary>
public sealed class StaticPipSource : IPipSource
{
    private readonly Dictionary<string, Dictionary<string, object>> _staticValues;

    /// <summary>
    /// Gets the source type identifier exposed through health and provenance metadata.
    /// </summary>
    public string SourceType => "static";

    /// <summary>
    /// Gets the logical source identifier.
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    /// Gets the attribute names available in the configured static dataset.
    /// </summary>
    public IReadOnlySet<string> ProvidesAttributes { get; }

    /// <summary>
    /// Gets the source priority used by the PIP resolver.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Initializes a static in-memory PIP source for tests and local development.
    /// </summary>
    public StaticPipSource(
        string sourceId,
        int priority,
        Dictionary<string, Dictionary<string, object>> staticValues)
    {
        SourceId = sourceId;
        Priority = priority;
        _staticValues = staticValues;
        ProvidesAttributes = staticValues.Values
            .SelectMany(v => v.Keys)
            .ToHashSet();
    }

    /// <summary>
    /// Resolves attributes from the configured in-memory subject map.
    /// </summary>
    public Task<AttributeResolutionResult> ResolveAsync(
        AttributeResolutionRequest request, CancellationToken ct = default)
    {
        var values = new List<AttributeValue>();

        if (_staticValues.TryGetValue(request.SubjectId, out var subjectAttrs))
        {
            foreach (var attrName in request.RequestedAttributes)
            {
                if (subjectAttrs.TryGetValue(attrName, out var value))
                {
                    values.Add(new AttributeValue
                    {
                        Name = attrName,
                        Category = AttributeCategory.Subject,
                        Value = value,
                        SourceId = SourceId,
                        SourceType = SourceType,
                        FetchedAt = DateTimeOffset.UtcNow,
                        CacheTtl = TimeSpan.FromSeconds(3600)
                    });
                }
            }
        }

        return Task.FromResult(AttributeResolutionResult.Succeeded(values));
    }

    /// <summary>
    /// Reports a healthy result because static sources do not require external connectivity.
    /// </summary>
    public Task<SourceHealthResult> TestConnectivityAsync(CancellationToken ct = default)
        => Task.FromResult(new SourceHealthResult
        {
            Healthy = true,
            Message = "Static source always healthy",
            ResponseTime = TimeSpan.Zero
        });
}
