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

    public string SourceType => "static";
    /// <summary>Gets the source Id.</summary>
    public string SourceId { get; }
    /// <summary>Gets the provides Attributes.</summary>
    public IReadOnlySet<string> ProvidesAttributes { get; }
    /// <summary>Gets the priority.</summary>
    public int Priority { get; }

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
    /// Executes resolve Async.
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
    /// Executes test Connectivity Async.
    /// </summary>
    public Task<SourceHealthResult> TestConnectivityAsync(CancellationToken ct = default)
        => Task.FromResult(new SourceHealthResult
        {
            Healthy = true,
            Message = "Static source always healthy",
            ResponseTime = TimeSpan.Zero
        });
}
