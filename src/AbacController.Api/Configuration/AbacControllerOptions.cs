namespace AbacController.Api.Configuration;

/// <summary>
/// Root configuration options for the ABAC Controller.
/// </summary>
public sealed class AbacControllerOptions
{
    /// <summary>Database configuration.</summary>
    public DatabaseOptions Database { get; set; } = new();

    /// <summary>Authentication and authorization options.</summary>
    public AuthOptions Auth { get; set; } = new();

    /// <summary>PDP engine configuration.</summary>
    public PdpOptions Pdp { get; set; } = new();

    /// <summary>Rate limiting configuration.</summary>
    public RateLimitingOptions RateLimiting { get; set; } = new();
}

/// <summary>
/// Database provider and connection string configuration.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Database provider: "sqlite", "postgres", or "sqlserver".</summary>
    public string Provider { get; set; } = "sqlite";

    /// <summary>Provider-specific connection string.</summary>
    public string? ConnectionString { get; set; }
}

/// <summary>
/// Authentication configuration for the ABAC Controller API.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>OAuth 2.0 / OpenID Connect authority URL.</summary>
    public string Authority { get; set; } = "";

    /// <summary>Expected JWT audience claim.</summary>
    public string Audience { get; set; } = "abac-controller";

    /// <summary>Whether to require HTTPS for metadata discovery.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Path to a local JWKS file for offline token validation.</summary>
    public string? JwksFile { get; set; }

    /// <summary>Enable development authentication handler (bypasses real auth).</summary>
    public bool EnableDevelopmentAuth { get; set; }

    /// <summary>Configured API keys for machine-to-machine authentication.</summary>
    public List<ApiKeyConfig> ApiKeys { get; set; } = [];
}

/// <summary>
/// Static API key configuration for machine-to-machine callers.
/// </summary>
public sealed class ApiKeyConfig
{
    /// <summary>The API key value (secret).</summary>
    public required string Key { get; set; }

    /// <summary>Client identifier for audit trail.</summary>
    public required string ClientId { get; set; }

    /// <summary>Scopes granted to this API key.</summary>
    public List<string> Scopes { get; set; } = [];

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// PDP engine configuration options.
/// </summary>
public sealed class PdpOptions
{
    /// <summary>Decision cache TTL in seconds (default 300).</summary>
    public int DecisionCacheTtlSeconds { get; set; } = 300;

    /// <summary>Whether PDP decision caching is enabled.</summary>
    public bool DecisionCacheEnabled { get; set; } = true;
}

/// <summary>
/// Rate limiting options for the PDP evaluation endpoints.
/// </summary>
public sealed class RateLimitingOptions
{
    /// <summary>Global permit limit per window for the PDP partition.</summary>
    public int PdpPermitLimit { get; set; } = 1000;

    /// <summary>Window size in seconds for the global rate limiter.</summary>
    public int WindowSeconds { get; set; } = 1;

    /// <summary>Per-client permit limit per window (0 = no per-client limit).</summary>
    public int PerClientPermitLimit { get; set; } = 200;

    /// <summary>Per-client window size in seconds.</summary>
    public int PerClientWindowSeconds { get; set; } = 1;

    /// <summary>Per-resource permit limit per window (0 = no per-resource limit).</summary>
    public int PerResourcePermitLimit { get; set; } = 100;

    /// <summary>Per-resource window size in seconds.</summary>
    public int PerResourceWindowSeconds { get; set; } = 1;
}
