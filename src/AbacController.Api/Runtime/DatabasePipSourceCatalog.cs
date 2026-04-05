using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using AbacController.Core.Domain.Attributes;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Entities;
using AbacController.Pip.Sources;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Api.Runtime;

/// <summary>
/// Builds runtime PIP source instances from persisted source definitions.
/// </summary>
public sealed class DatabasePipSourceCatalog : IPipSourceCatalog
{
    private readonly AbacDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>Initializes a new instance of the <see cref="DatabasePipSourceCatalog"/> class.</summary>
    public DatabasePipSourceCatalog(AbacDbContext dbContext, IHttpClientFactory httpClientFactory)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IPipSource>> GetSourcesAsync(CancellationToken ct = default)
    {
        var entities = await _dbContext.PipSources
            .AsNoTracking()
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        return entities.Select(CreateSource).ToList();
    }

    private IPipSource CreateSource(PipSourceEntity entity)
    {
        try
        {
            return entity.SourceType.ToLowerInvariant() switch
            {
                "static" => CreateStaticSource(entity),
                "file" => CreateFileSource(entity),
                "rest" => CreateRestSource(entity),
                "oidc" => CreateOidcSource(entity),
                "ldap" => CreateLdapSource(entity),
                _ => new InvalidPipSource(entity, $"Unsupported sourceType '{entity.SourceType}'.")
            };
        }
        catch (Exception ex)
        {
            return new InvalidPipSource(entity, ex.Message);
        }
    }

    private IPipSource CreateStaticSource(PipSourceEntity entity)
    {
        using var doc = ParseConfig(entity);
        var root = doc.RootElement;

        Dictionary<string, Dictionary<string, object>> subjects;
        if (TryGetProperty(root, "subjects", out var subjectsElement) && subjectsElement.ValueKind == JsonValueKind.Object)
        {
            subjects = ParseSubjects(subjectsElement);
        }
        else if (TryGetProperty(root, "staticValues", out var staticValuesElement) && staticValuesElement.ValueKind == JsonValueKind.Object)
        {
            subjects = ParseSubjects(staticValuesElement);
        }
        else if (TryGetProperty(root, "attributes", out var attributesElement) && attributesElement.ValueKind == JsonValueKind.Object)
        {
            subjects = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase)
            {
                ["*"] = ParseAttributes(attributesElement)
            };
        }
        else
        {
            throw new InvalidOperationException("Static source config must contain 'subjects', 'staticValues', or 'attributes'.");
        }

        return new StaticPipSource(entity.Id, entity.Priority, subjects);
    }

    private IPipSource CreateFileSource(PipSourceEntity entity)
    {
        using var doc = ParseConfig(entity);
        var root = doc.RootElement;
        var filePath = GetRequiredString(root, "filePath", "path");
        return new FilePipSource(entity.Id, entity.Priority, filePath, BuildCacheTtl(entity));
    }

    private IPipSource CreateRestSource(PipSourceEntity entity)
    {
        using var doc = ParseConfig(entity);
        var root = doc.RootElement;
        var urlTemplate = GetRequiredString(root, "urlTemplate", "url", "endpoint");
        return new RestPipSource(
            entity.Id,
            entity.Priority,
            urlTemplate,
            ParseProvidesAttributes(entity),
            _httpClientFactory.CreateClient(),
            BuildCacheTtl(entity));
    }

    private IPipSource CreateOidcSource(PipSourceEntity entity)
    {
        using var doc = ParseConfig(entity);
        var root = doc.RootElement;
        if (!TryGetProperty(root, "claimMapping", out var claimMappingElement) || claimMappingElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("OIDC source config must contain 'claimMapping'.");
        }

        var claimMapping = claimMappingElement.EnumerateObject()
            .Where(static p => p.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);

        return new OidcPipSource(
            entity.Id,
            entity.Priority,
            GetRequiredString(root, "userInfoEndpoint", "url", "endpoint"),
            claimMapping,
            _httpClientFactory.CreateClient(),
            BuildCacheTtl(entity));
    }

    private IPipSource CreateLdapSource(PipSourceEntity entity)
    {
        using var doc = ParseConfig(entity);
        var root = doc.RootElement;

        var host = GetRequiredString(root, "host");
        var port = TryGetInt(root, "port") ?? 389;
        var useSsl = TryGetBool(root, "useSsl") ?? false;
        var baseDn = GetRequiredString(root, "baseDn");
        var subjectIdAttribute = GetRequiredString(root, "subjectIdAttribute", "subjectIdField", "lookupAttribute");
        var username = TryGetString(root, "username", "bindDn");
        var password = TryGetString(root, "password");

        NetworkCredential? credential = null;
        if (!string.IsNullOrWhiteSpace(username))
        {
            credential = new NetworkCredential(username, password ?? string.Empty);
        }

        return new LdapPipSource(
            entity.Id,
            entity.Priority,
            host,
            port,
            useSsl,
            baseDn,
            subjectIdAttribute,
            ParseProvidesAttributes(entity),
            credential,
            BuildCacheTtl(entity));
    }

    private static JsonDocument ParseConfig(PipSourceEntity entity)
    {
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(entity.ConfigJson) ? "{}" : entity.ConfigJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid configJson: {ex.Message}");
        }
    }

    private static Dictionary<string, Dictionary<string, object>> ParseSubjects(JsonElement element)
    {
        var result = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        foreach (var subject in element.EnumerateObject())
        {
            if (subject.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            result[subject.Name] = ParseAttributes(subject.Value);
        }

        return result;
    }

    private static Dictionary<string, object> ParseAttributes(JsonElement element)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ConvertAttributeValue(property.Name, property.Value);
        }

        return result;
    }

    private static object ConvertAttributeValue(string attributeName, JsonElement value)
    {
        if (string.Equals(attributeName, "securityClearance", StringComparison.Ordinal) && value.ValueKind == JsonValueKind.Object)
        {
            return ParseSecurityClearance(value);
        }

        if (string.Equals(attributeName, "securityLabel", StringComparison.Ordinal) && value.ValueKind == JsonValueKind.Object)
        {
            return ParseSecurityLabel(value);
        }

        return ConvertJsonValue(value);
    }

    private static object ConvertJsonValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number when value.TryGetInt64(out var longVal) => longVal,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => value.EnumerateArray().Select(ConvertJsonValue).ToList(),
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonValue(p.Value), StringComparer.Ordinal),
            _ => value.GetRawText()
        };

    private static SecurityClearance ParseSecurityClearance(JsonElement element)
    {
        var policyOid = GetRequiredString(element, "policyOid");
        var classifications = TryGetProperty(element, "classificationLacvs", out var classificationElement) && classificationElement.ValueKind == JsonValueKind.Array
            ? classificationElement.EnumerateArray().Where(static item => item.TryGetInt32(out _)).Select(item => new LacvValue(item.GetInt32())).ToImmutableHashSet()
            : ImmutableHashSet<LacvValue>.Empty;

        return new SecurityClearance
        {
            PolicyOid = policyOid,
            ClassificationLacvs = classifications
        };
    }

    private static SecurityLabel ParseSecurityLabel(JsonElement element)
    {
        var classificationLacv = TryGetInt(element, "classificationLacv") ?? 0;
        return new SecurityLabel
        {
            PolicyOid = TryGetString(element, "policyOid"),
            PolicyName = TryGetString(element, "policyName"),
            ClassificationName = TryGetString(element, "classificationName"),
            ClassificationLacv = new LacvValue(classificationLacv)
        };
    }

    private static IReadOnlySet<string> ParseProvidesAttributes(PipSourceEntity entity)
        => entity.ProvidesAttributes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    private static TimeSpan? BuildCacheTtl(PipSourceEntity entity)
        => entity.CacheEnabled ? TimeSpan.FromSeconds(Math.Max(1, entity.CacheTtlSeconds)) : TimeSpan.Zero;

    private static string GetRequiredString(JsonElement element, params string[] names)
        => TryGetString(element, names) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Config must contain one of: {string.Join(", ", names)}");

    private static string? TryGetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static int? TryGetInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var property) && property.TryGetInt32(out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool? TryGetBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var property) && (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False))
            {
                return property.GetBoolean();
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed class InvalidPipSource : IPipSource
    {
        private readonly string _message;

        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidPipSource"/> class for a source definition that could not be materialized.
        /// </summary>
        /// <param name="entity">The source entity that failed validation or configuration parsing.</param>
        /// <param name="message">The reason the source is considered invalid.</param>
        public InvalidPipSource(PipSourceEntity entity, string message)
        {
            SourceId = entity.Id;
            SourceType = entity.SourceType;
            Priority = entity.Priority;
            ProvidesAttributes = ParseProvidesAttributes(entity);
            _message = message;
        }

        /// <summary>
        /// Gets the configured source type for the invalid source entry.
        /// </summary>
        public string SourceType { get; }

        /// <summary>
        /// Gets the identifier of the invalid source entry.
        /// </summary>
        public string SourceId { get; }

        /// <summary>
        /// Gets the attributes the source declared it can provide.
        /// </summary>
        public IReadOnlySet<string> ProvidesAttributes { get; }

        /// <summary>
        /// Gets the configured source priority.
        /// </summary>
        public int Priority { get; }

        /// <summary>
        /// Returns a failed resolution result explaining that the source configuration is invalid.
        /// </summary>
        /// <param name="request">The attribute resolution request.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>A failed attribute resolution result.</returns>
        public Task<AttributeResolutionResult> ResolveAsync(AttributeResolutionRequest request, CancellationToken ct = default)
            => Task.FromResult(AttributeResolutionResult.Failed($"Source '{SourceId}' is invalid: {_message}"));

        /// <summary>
        /// Returns an unhealthy health check result for the invalid source configuration.
        /// </summary>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>An unhealthy source health result.</returns>
        public Task<SourceHealthResult> TestConnectivityAsync(CancellationToken ct = default)
            => Task.FromResult(new SourceHealthResult
            {
                Healthy = false,
                Message = _message,
                ResponseTime = TimeSpan.Zero
            });
    }
}
