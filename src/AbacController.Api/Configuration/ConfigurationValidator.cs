namespace AbacController.Api.Configuration;

/// <summary>
/// Validates ABAC Controller configuration on startup, failing fast with clear messages.
/// </summary>
public static class ConfigurationValidator
{
    /// <summary>
    /// Validate the configuration and return a list of errors.
    /// Empty list means valid.
    /// </summary>
    public static List<string> Validate(AbacControllerOptions options, bool isDevelopment)
    {
        var errors = new List<string>();

        // Database validation
        if (string.IsNullOrWhiteSpace(options.Database.Provider))
        {
            errors.Add("Database:Provider is required.");
        }

        // Auth validation
        var hasAuthority = !string.IsNullOrWhiteSpace(options.Auth.Authority);
        var hasApiKeys = options.Auth.ApiKeys.Count > 0;
        var allowDev = isDevelopment && options.Auth.EnableDevelopmentAuth;

        if (!hasAuthority && !hasApiKeys && !allowDev)
        {
            errors.Add("Authentication is not configured. Set Auth:Authority for JWT, configure Auth:ApiKeys, or enable Auth:EnableDevelopmentAuth in Development.");
        }

        if (options.Auth.EnableDevelopmentAuth && !isDevelopment)
        {
            errors.Add("Auth:EnableDevelopmentAuth is true but environment is not Development. This is a security risk.");
        }

        // API key validation
        foreach (var (apiKey, index) in options.Auth.ApiKeys.Select((k, i) => (k, i)))
        {
            if (string.IsNullOrWhiteSpace(apiKey.Key))
                errors.Add($"Auth:ApiKeys[{index}]:Key is required.");
            if (string.IsNullOrWhiteSpace(apiKey.ClientId))
                errors.Add($"Auth:ApiKeys[{index}]:ClientId is required.");
            if (apiKey.Key?.Length < 32 && !isDevelopment)
                errors.Add($"Auth:ApiKeys[{index}]:Key should be at least 32 characters for production use.");
        }

        // Rate limiting validation
        if (options.RateLimiting.PdpPermitLimit <= 0)
            errors.Add("RateLimiting:PdpPermitLimit must be positive.");
        if (options.RateLimiting.WindowSeconds <= 0)
            errors.Add("RateLimiting:WindowSeconds must be positive.");

        // PDP validation
        if (options.Pdp.DecisionCacheTtlSeconds < 0)
            errors.Add("Pdp:DecisionCacheTtlSeconds must not be negative.");

        return errors;
    }
}
