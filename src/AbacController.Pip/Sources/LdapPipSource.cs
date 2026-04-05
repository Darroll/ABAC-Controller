using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.Net;
using AbacController.Core.Domain.Attributes;
using AbacController.Core.Interfaces;

namespace AbacController.Pip.Sources;

/// <summary>
/// LDAP-backed PIP source for resolving subject attributes from directory services.
/// </summary>
public sealed class LdapPipSource : IPipSource, IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly bool _useSsl;
    private readonly string _baseDn;
    private readonly string _subjectIdAttribute;
    private readonly NetworkCredential? _credential;
    private readonly TimeSpan _cacheTtl;

    /// <summary>
    /// Gets the source type identifier exposed through health and provenance metadata.
    /// </summary>
    public string SourceType => "ldap";

    /// <summary>
    /// Gets the logical source identifier.
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    /// Gets the attribute names that this LDAP source can return.
    /// </summary>
    public IReadOnlySet<string> ProvidesAttributes { get; }

    /// <summary>
    /// Gets the source priority used by the PIP resolver.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Initializes an LDAP-backed PIP source.
    /// </summary>
    /// <summary>Initializes a new instance of the <see cref="LdapPipSource"/> class.</summary>
    public LdapPipSource(
        string sourceId,
        int priority,
        string host,
        int port,
        bool useSsl,
        string baseDn,
        string subjectIdAttribute,
        IReadOnlySet<string> providesAttributes,
        NetworkCredential? credential = null,
        TimeSpan? cacheTtl = null)
    {
        SourceId = sourceId;
        Priority = priority;
        _host = host;
        _port = port;
        _useSsl = useSsl;
        _baseDn = baseDn;
        _subjectIdAttribute = subjectIdAttribute;
        ProvidesAttributes = providesAttributes;
        _credential = credential;
        _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(300);
    }

    /// <summary>
    /// Resolves subject attributes by querying the configured LDAP directory.
    /// </summary>
    public async Task<AttributeResolutionResult> ResolveAsync(
        AttributeResolutionRequest request,
        CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(() => ResolveInternal(request), ct);
        }
        catch (Exception ex)
        {
            return AttributeResolutionResult.Failed($"LDAP source error: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies that the LDAP endpoint can be contacted and bound successfully.
    /// </summary>
    public async Task<SourceHealthResult> TestConnectivityAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await Task.Run(() =>
            {
                using var connection = CreateConnection();
                connection.Bind();
            }, ct);

            sw.Stop();
            return new SourceHealthResult
            {
                Healthy = true,
                Message = "OK",
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

    private AttributeResolutionResult ResolveInternal(AttributeResolutionRequest request)
    {
        using var connection = CreateConnection();
        connection.Bind();

        var filter = $"({_subjectIdAttribute}={EscapeLdapFilterValue(request.SubjectId)})";
        var attributes = request.RequestedAttributes.Distinct().ToArray();
        var searchRequest = new SearchRequest(
            _baseDn,
            filter,
            SearchScope.Subtree,
            attributes);

        var response = (SearchResponse)connection.SendRequest(searchRequest);
        var entry = response.Entries.Cast<SearchResultEntry>().FirstOrDefault();
        if (entry is null)
        {
            return new AttributeResolutionResult
            {
                Success = false,
                Missing = request.RequestedAttributes.ToList(),
                Error = $"No LDAP entry found for subject '{request.SubjectId}'"
            };
        }

        var values = new List<AttributeValue>();
        var missing = new List<string>();

        foreach (var attributeName in request.RequestedAttributes)
        {
            if (!entry.Attributes.Contains(attributeName))
            {
                missing.Add(attributeName);
                continue;
            }

            var attribute = entry.Attributes[attributeName];
            if (attribute is null || attribute.Count == 0)
            {
                missing.Add(attributeName);
                continue;
            }

            object value = attribute.Count == 1
                ? ConvertValue(attribute[0])
                : attribute.GetValues(typeof(string)).Cast<object>().ToArray();

            values.Add(new AttributeValue
            {
                Name = attributeName,
                Category = AttributeCategory.Subject,
                Value = value,
                SourceId = SourceId,
                SourceType = SourceType,
                FetchedAt = DateTimeOffset.UtcNow,
                CacheTtl = _cacheTtl
            });
        }

        return new AttributeResolutionResult
        {
            Success = missing.Count == 0,
            Values = values,
            Missing = missing
        };
    }

    private LdapConnection CreateConnection()
    {
        var identifier = new LdapDirectoryIdentifier(_host, _port, false, false);
        var connection = new LdapConnection(identifier)
        {
            AuthType = _credential is null ? AuthType.Anonymous : AuthType.Basic,
            Credential = _credential
        };

        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = _useSsl;
        connection.Timeout = TimeSpan.FromSeconds(10);
        return connection;
    }

    private static object ConvertValue(object raw)
    {
        return raw switch
        {
            byte[] bytes => bytes,
            string s => s,
            _ => raw.ToString() ?? string.Empty
        };
    }

    private static string EscapeLdapFilterValue(string value)
    {
        return value
            .Replace("\\", "\\5c", StringComparison.Ordinal)
            .Replace("*", "\\2a", StringComparison.Ordinal)
            .Replace("(", "\\28", StringComparison.Ordinal)
            .Replace(")", "\\29", StringComparison.Ordinal)
            .Replace("\0", "\\00", StringComparison.Ordinal);
    }

    /// <summary>
    /// Releases this instance.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
