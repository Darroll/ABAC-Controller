using AbacController.Api.Configuration;

namespace AbacController.Tests.Unit;

/// <summary>
/// Edge case tests for ConfigurationValidator — boundary conditions and error messages.
/// </summary>
public sealed class ConfigurationValidatorEdgeCaseTests
{
    [Fact]
    public void Validate_ValidDevConfig_NoErrors()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions { EnableDevelopmentAuth = true }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: true);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_DevAuthInProduction_ReturnsError()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions
            {
                EnableDevelopmentAuth = true,
                ApiKeys = [new ApiKeyConfig { Key = "a".PadRight(32, 'x'), ClientId = "test", Scopes = [] }]
            }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: false);
        Assert.Contains(errors, e => e.Contains("EnableDevelopmentAuth") && e.Contains("not Development"));
    }

    [Fact]
    public void Validate_NoAuth_ReturnsError()
    {
        var options = new AbacControllerOptions();

        var errors = ConfigurationValidator.Validate(options, isDevelopment: false);
        Assert.Contains(errors, e => e.Contains("Authentication is not configured"));
    }

    [Fact]
    public void Validate_ShortApiKeyInProd_ReturnsError()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions
            {
                ApiKeys = [new ApiKeyConfig { Key = "short", ClientId = "test", Scopes = [] }]
            }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: false);
        Assert.Contains(errors, e => e.Contains("at least 32 characters"));
    }

    [Fact]
    public void Validate_ShortApiKeyInDev_NoKeyLengthError()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions
            {
                EnableDevelopmentAuth = true,
                ApiKeys = [new ApiKeyConfig { Key = "short", ClientId = "test", Scopes = [] }]
            }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: true);
        Assert.DoesNotContain(errors, e => e.Contains("at least 32 characters"));
    }

    [Fact]
    public void Validate_ZeroRateLimit_ReturnsError()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions { EnableDevelopmentAuth = true },
            RateLimiting = new RateLimitingOptions { PdpPermitLimit = 0 }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: true);
        Assert.Contains(errors, e => e.Contains("PdpPermitLimit must be positive"));
    }

    [Fact]
    public void Validate_NegativeCacheTtl_ReturnsError()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions { EnableDevelopmentAuth = true },
            Pdp = new PdpOptions { DecisionCacheTtlSeconds = -1 }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: true);
        Assert.Contains(errors, e => e.Contains("DecisionCacheTtlSeconds must not be negative"));
    }

    [Fact]
    public void Validate_EmptyDatabaseProvider_ReturnsError()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions { EnableDevelopmentAuth = true },
            Database = new DatabaseOptions { Provider = "" }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: true);
        Assert.Contains(errors, e => e.Contains("Database:Provider is required"));
    }

    [Fact]
    public void Validate_ApiKeyMissingClientId_ReturnsError()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions
            {
                EnableDevelopmentAuth = true,
                ApiKeys = [new ApiKeyConfig { Key = "valid-key-long-enough", ClientId = "", Scopes = [] }]
            }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: true);
        Assert.Contains(errors, e => e.Contains("ClientId is required"));
    }

    [Fact]
    public void Validate_ValidProductionConfig_NoErrors()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions
            {
                Authority = "https://auth.example.com",
                Audience = "abac-controller"
            }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: false);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ZeroWindowSeconds_ReturnsError()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions { EnableDevelopmentAuth = true },
            RateLimiting = new RateLimitingOptions { WindowSeconds = 0 }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: true);
        Assert.Contains(errors, e => e.Contains("WindowSeconds must be positive"));
    }
}
