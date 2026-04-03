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
}

public sealed class PdpOptions
{
    public int DecisionCacheTtlSeconds { get; set; } = 300;
    public bool DecisionCacheEnabled { get; set; } = true;
}

public sealed class RateLimitingOptions
{
    public int PdpPermitLimit { get; set; } = 1000;
    public int WindowSeconds { get; set; } = 1;
}
