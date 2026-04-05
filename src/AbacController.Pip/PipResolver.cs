using AbacController.Core.Domain.Attributes;
using AbacController.Core.Interfaces;

namespace AbacController.Pip;

/// <summary>
/// Attribute resolution orchestrator implementing the 3-layer strategy:
/// Layer 1: Request context (inline), Layer 2: Cache, Layer 3: External PIP sources.
/// </summary>
public sealed class PipResolver : IPipResolver
{
    private readonly IEnumerable<IPipSource> _sources;
    private readonly IPipSourceCatalog? _sourceCatalog;
    private readonly IPipCacheManager _cache;

    /// <summary>Initializes a new instance of the <see cref="PipResolver"/> class.</summary>
    public PipResolver(IPipCacheManager cache, IPipSourceCatalog? sourceCatalog = null, IEnumerable<IPipSource>? sources = null)
    {
        _cache = cache;
        _sourceCatalog = sourceCatalog;
        _sources = sources ?? [];
    }

    /// <inheritdoc />
    public async Task<AttributeResolutionResult> ResolveAsync(
        AttributeResolutionRequest request, CancellationToken ct = default)
    {
        var sources = await GetSourcesAsync(ct);
        var resolved = new List<AttributeValue>();
        var missing = new List<string>(request.RequestedAttributes);
        var toFetch = new List<string>();

        // Layer 2: Check cache for each attribute
        foreach (var attrName in request.RequestedAttributes)
        {
            var found = false;
            foreach (var source in sources.OrderBy(s => s.Priority))
            {
                if (_cache.TryGet(source.SourceId, request.SubjectId, attrName, out var cached)
                    && cached is not null)
                {
                    resolved.Add(cached with { FromCache = true });
                    missing.Remove(attrName);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                toFetch.Add(attrName);
            }
        }

        if (toFetch.Count == 0)
        {
            return AttributeResolutionResult.Succeeded(resolved);
        }

        // Layer 3: Fetch from external sources
        var fetchRequest = request with { RequestedAttributes = toFetch };

        foreach (var source in sources.OrderBy(s => s.Priority))
        {
            if (toFetch.Count == 0)
            {
                break;
            }

            var providable = toFetch
                .Where(a => source.ProvidesAttributes.Contains(a))
                .ToList();

            if (providable.Count == 0)
            {
                continue;
            }

            try
            {
                var sourceRequest = fetchRequest with { RequestedAttributes = providable };
                var result = await source.ResolveAsync(sourceRequest, ct);

                if (result.Success)
                {
                    foreach (var value in result.Values)
                    {
                        resolved.Add(value);
                        toFetch.Remove(value.Name);
                        missing.Remove(value.Name);

                        // Cache the result
                        _cache.Set(source.SourceId, request.SubjectId, value,
                            value.CacheTtl ?? TimeSpan.FromSeconds(300));
                    }
                }
            }
            catch
            {
                // Source failed — try next one
            }
        }

        return new AttributeResolutionResult
        {
            Success = missing.Count == 0,
            Values = resolved,
            Missing = missing
        };
    }

    private async Task<IReadOnlyList<IPipSource>> GetSourcesAsync(CancellationToken ct)
    {
        if (_sourceCatalog is not null)
        {
            return await _sourceCatalog.GetSourcesAsync(ct);
        }

        return _sources.ToList();
    }
}
