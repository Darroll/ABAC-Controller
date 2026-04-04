using AbacController.Api.Configuration;

namespace AbacController.Tests.Unit;

public sealed class ConfigurationValidatorTests
{
    [Fact]
    public void Validate_AcceptsDevAuthInDevelopment()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions { EnableDevelopmentAuth = true }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: true);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsDevAuthInProduction()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions { EnableDevelopmentAuth = true }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: false);
        Assert.Contains(errors, e => e.Contains("security risk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AcceptsApiKeyAuth()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions
            {
                ApiKeys =
                [
                    new ApiKeyConfig
                    {
                        Key = "a-very-long-secret-key-at-least-32-characters-long",
                        ClientId = "test-client",
                        Scopes = ["abac:evaluate"]
                    }
                ]
            }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: false);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsShortApiKeyInProduction()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions
            {
                ApiKeys =
                [
                    new ApiKeyConfig
                    {
                        Key = "short",
                        ClientId = "test-client"
                    }
                ]
            }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: false);
        Assert.Contains(errors, e => e.Contains("32 characters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AcceptsJwtAuth()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions { Authority = "https://idp.example.com" }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: false);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsNoAuth()
    {
        var options = new AbacControllerOptions();

        var errors = ConfigurationValidator.Validate(options, isDevelopment: false);
        Assert.Contains(errors, e => e.Contains("Authentication is not configured"));
    }

    [Fact]
    public void Validate_RejectsInvalidRateLimiting()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions { Authority = "https://idp.example.com" },
            RateLimiting = new RateLimitingOptions { PdpPermitLimit = 0, WindowSeconds = -1 }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: false);
        Assert.Contains(errors, e => e.Contains("PdpPermitLimit"));
        Assert.Contains(errors, e => e.Contains("WindowSeconds"));
    }

    [Fact]
    public void Validate_RejectsNegativeCacheTtl()
    {
        var options = new AbacControllerOptions
        {
            Auth = new AuthOptions { Authority = "https://idp.example.com" },
            Pdp = new PdpOptions { DecisionCacheTtlSeconds = -1 }
        };

        var errors = ConfigurationValidator.Validate(options, isDevelopment: false);
        Assert.Contains(errors, e => e.Contains("DecisionCacheTtlSeconds"));
    }
}
