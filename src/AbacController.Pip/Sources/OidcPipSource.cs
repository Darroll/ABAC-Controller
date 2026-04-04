using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Core.Domain.Attributes;
using AbacController.Core.Interfaces;

namespace AbacController.Pip.Sources;

/// <summary>
/// OIDC/OAuth PIP source. Fetches user attributes from an OIDC UserInfo endpoint
/// or extracts them from JWT token claims. Requires a bearer token in the
/// evaluation request context.
/// </summary>
public sealed class OidcPipSource : IPipSource
{
    private readonly HttpClient _httpClient;
    private readonly string _userInfoEndpoint;
    private readonly Dictionary<string, string> _claimMapping;
    private readonly TimeSpan _cacheTtl;

    public string SourceType => "oidc";
    public string SourceId { get; }
    public IReadOnlySet<string> ProvidesAttributes { get; }
    public int Priority { get; }

    /// <param name="sourceId">Source instance ID.</param>
    /// <param name="priority">Resolution priority.</param>
    /// <param name="userInfoEndpoint">OIDC UserInfo endpoint URL.</param>
    /// <param name="claimMapping">Maps OIDC claim names to attribute names. Keys = claim names, values = attribute names.</param>
    /// <param name="httpClient">HTTP client for UserInfo requests.</param>
    /// <param name="cacheTtl">How long to cache resolved attributes.</param>
    public OidcPipSource(
        string sourceId,
        int priority,
        string userInfoEndpoint,
        Dictionary<string, string> claimMapping,
        HttpClient httpClient,
        TimeSpan? cacheTtl = null)
    {
        SourceId = sourceId;
        Priority = priority;
        _userInfoEndpoint = userInfoEndpoint;
        _claimMapping = claimMapping;
        _httpClient = httpClient;
        _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(300);
        ProvidesAttributes = claimMapping.Values.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<AttributeResolutionResult> ResolveAsync(
        AttributeResolutionRequest request, CancellationToken ct = default)
    {
        try
        {
            // Extract bearer token from context
            var bearerToken = request.Context.TryGetValue("bearerToken", out var tokenObj)
                ? tokenObj?.ToString()
                : null;

            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                return AttributeResolutionResult.Failed("No bearer token in request context for OIDC resolution");
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, _userInfoEndpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

            var response = await _httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                return AttributeResolutionResult.Failed($"OIDC UserInfo returned {response.StatusCode}");
            }

            var userInfo = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var values = new List<AttributeValue>();

            foreach (var attrName in request.RequestedAttributes)
            {
                // Find the claim that maps to this attribute
                var claimName = _claimMapping
                    .Where(kvp => string.Equals(kvp.Value, attrName, StringComparison.Ordinal))
                    .Select(static kvp => kvp.Key)
                    .FirstOrDefault();

                if (claimName is null)
                    continue;

                if (userInfo.TryGetProperty(claimName, out var claimValue))
                {
                    values.Add(new AttributeValue
                    {
                        Name = attrName,
                        Category = AttributeCategory.Subject,
                        Value = ExtractValue(claimValue),
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
            return AttributeResolutionResult.Failed($"OIDC UserInfo error: {ex.Message}");
        }
    }

    public async Task<SourceHealthResult> TestConnectivityAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Just check if the endpoint responds (OPTIONS or HEAD)
            using var request = new HttpRequestMessage(HttpMethod.Options, _userInfoEndpoint);
            var response = await _httpClient.SendAsync(request, ct);
            sw.Stop();

            return new SourceHealthResult
            {
                Healthy = true, // Even 401 means the endpoint is up
                Message = $"Endpoint reachable (status: {response.StatusCode})",
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

    private static object ExtractValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number when element.TryGetInt64(out var longVal) => longVal,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(static e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText())
                .ToList(),
            _ => element.GetRawText()
        };
    }
}
