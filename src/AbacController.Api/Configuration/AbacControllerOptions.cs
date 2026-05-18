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

    /// <summary>Default location of the bundled SPIF import directory.</summary>
    public SeedOptions Seed { get; set; } = new();
}

/// <summary>
/// Configures the bundled SPIF import directory used by
/// <c>POST /pap/api/spifs/bundled/import</c>. Startup no longer auto-seeds;
/// operators drive imports explicitly via the admin endpoint.
/// </summary>
public sealed class SeedOptions
{
    /// <summary>
    /// Retained for backward compatibility with existing configuration
    /// files, but no longer drives automatic behaviour. The bundled
    /// SPIF import is always an explicit admin-API action now.
    /// </summary>
    public bool SeedDefaultSpifs { get; set; }

    /// <summary>
    /// Directory the bundled importer scans. Defaults to
    /// <c>{AppContext.BaseDirectory}/data/seed-spifs</c> which matches
    /// the path the API project copies the bundled defaults to via the
    /// csproj Content item (and thus <c>/app/data/seed-spifs</c> in the
    /// shipped container image).
    /// </summary>
    public string? SpifSeedDirectory { get; set; }
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

    /// <summary>
    /// Optional secondary JWT issuer for Keycloak. When <see cref="KeycloakAuthOptions.Authority"/>
    /// is set the host registers an additional <c>"Keycloak"</c> bearer scheme alongside
    /// the primary issuer; authorization policies accept either scheme.
    /// </summary>
    public KeycloakAuthOptions Keycloak { get; set; } = new();
}

/// <summary>
/// Configuration for the Keycloak JWT bearer scheme. Disabled by default —
/// when <see cref="Authority"/> is empty no second scheme is registered and
/// the host behaves exactly as before.
/// </summary>
public sealed class KeycloakAuthOptions
{
    /// <summary>OIDC issuer URL, e.g. <c>http://keycloak:8080/realms/abac-dev</c>.</summary>
    public string Authority { get; set; } = "";

    /// <summary>Expected <c>aud</c> claim value (typically the Keycloak client id).</summary>
    public string Audience { get; set; } = "abac-controller-api";

    /// <summary>
    /// Whether to require HTTPS for OIDC metadata discovery. Defaults to false
    /// for the dev compose stack; production deployments should set this to true.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; }

    /// <summary>True when the Keycloak scheme should be activated.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Authority);
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
