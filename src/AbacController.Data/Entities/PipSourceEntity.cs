namespace AbacController.Data.Entities;

/// <summary>
/// EF Core entity for a PIP attribute source configuration.
/// </summary>
public class PipSourceEntity
{
    /// <summary>Unique source identifier.</summary>
    public string Id { get; set; } = "";

    /// <summary>Human-readable source name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Source type: "ldap", "oidc", "rest", "file", or "static".</summary>
    public string SourceType { get; set; } = "";

    /// <summary>Source-specific configuration as JSON.</summary>
    public string ConfigJson { get; set; } = "{}";

    /// <summary>Comma-separated list of attribute names this source provides.</summary>
    public string ProvidesAttributes { get; set; } = "";

    /// <summary>Resolution priority (lower = tried first).</summary>
    public int Priority { get; set; }

    /// <summary>Whether this source is required (evaluation fails if unavailable).</summary>
    public bool IsRequired { get; set; }

    /// <summary>Whether resolved attributes are cached.</summary>
    public bool CacheEnabled { get; set; } = true;

    /// <summary>Cache TTL in seconds.</summary>
    public int CacheTtlSeconds { get; set; } = 300;

    /// <summary>Maximum number of cached attribute sets.</summary>
    public int CacheMaxEntries { get; set; } = 1000;

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last update timestamp (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
