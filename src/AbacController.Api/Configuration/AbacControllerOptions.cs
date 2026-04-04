namespace AbacController.Api.Configuration;

/// <summary>
/// Root configuration options for the ABAC Controller.
/// </summary>
public sealed class AbacControllerOptions
{
    public DatabaseOptions Database { get; set; } = new();
    public AuthOptions Auth { get; set; } = new();
    public PdpOptions Pdp { get; set; } = new();
    public RateLimitingOptions RateLimiting { get; set; } = new();
}

public sealed class DatabaseOptions
{
    public string Provider { get; set; } = "sqlite";
    public string? ConnectionString { get; set; }
}

public sealed class AuthOptions
{
    public string Authority { get; set; } = "";
    public string Audience { get; set; } = "abac-controller";
    public bool RequireHttpsMetadata { get; set; } = true;
    public string? JwksFile { get; set; }
    public bool EnableDevelopmentAuth { get; set; }
    public List<ApiKeyConfig> ApiKeys { get; set; } = [];
}

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

public sealed class PdpOptions
{
    public int DecisionCacheTtlSeconds { get; set; } = 300;
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
