using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Core.Domain.Attributes;
using AbacController.Core.Interfaces;

namespace AbacController.Pip.Sources;

/// <summary>
/// HTTP REST attribute source. Calls REST API to resolve subject attributes.
/// URL template supports {subjectId} placeholder.
/// </summary>
public sealed class RestPipSource : IPipSource
{
    private readonly HttpClient _httpClient;
    private readonly string _urlTemplate;
    private readonly TimeSpan _cacheTtl;

    public string SourceType => "rest";
    /// <summary>Gets the source Id.</summary>
    public string SourceId { get; }
    /// <summary>Gets the provides Attributes.</summary>
    public IReadOnlySet<string> ProvidesAttributes { get; }
    /// <summary>Gets the priority.</summary>
    public int Priority { get; }

    public RestPipSource(
        string sourceId,
        int priority,
        string urlTemplate,
        IReadOnlySet<string> providesAttributes,
        HttpClient httpClient,
        TimeSpan? cacheTtl = null)
    {
        SourceId = sourceId;
        Priority = priority;
        _urlTemplate = urlTemplate;
        ProvidesAttributes = providesAttributes;
        _httpClient = httpClient;
        _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(300);
    }

    /// <summary>
    /// Executes resolve Async.
    /// </summary>
    public async Task<AttributeResolutionResult> ResolveAsync(
        AttributeResolutionRequest request, CancellationToken ct = default)
    {
        try
        {
            var url = _urlTemplate.Replace("{subjectId}", Uri.EscapeDataString(request.SubjectId));
            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
                return AttributeResolutionResult.Failed(
                    $"REST source returned {response.StatusCode}");

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var values = new List<AttributeValue>();

            foreach (var attrName in request.RequestedAttributes)
            {
                if (json.TryGetProperty(attrName, out var prop))
                {
                    values.Add(new AttributeValue
                    {
                        Name = attrName,
                        Category = AttributeCategory.Subject,
                        Value = prop.ValueKind switch
                        {
                            JsonValueKind.String => prop.GetString()!,
                            JsonValueKind.Number => prop.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => prop.GetRawText()
                        },
                        SourceId = SourceId,
                        SourceType = SourceType,
                        FetchedAt = DateTimeOffset.UtcNow,
                        CacheTtl = _cacheTtl
                    });
                }
            }

            return AttributeResolutionResult.Succeeded(values);
        }
        catch (Exception ex)
        {
            return AttributeResolutionResult.Failed($"REST source error: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes test Connectivity Async.
    /// </summary>
    public async Task<SourceHealthResult> TestConnectivityAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var url = _urlTemplate.Replace("{subjectId}", "healthcheck");
            var response = await _httpClient.GetAsync(url, ct);
            sw.Stop();

            return new SourceHealthResult
            {
                Healthy = response.IsSuccessStatusCode,
                Message = response.IsSuccessStatusCode ? "OK" : $"Status: {response.StatusCode}",
                ResponseTime = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new SourceHealthResult
            {
                Healthy = false,
                Message = ex.Message,
                ResponseTime = sw.Elapsed
            };
        }
    }
}
