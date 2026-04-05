using System.Text.Encodings.Web;
using AbacController.Api.Auth;
using AbacController.Api.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AbacController.Tests.Unit;

/// <summary>
/// Unit tests for API key authentication behavior.
/// </summary>
public sealed class ApiKeyAuthHandlerTests
{
    [Fact]
    public async Task AuthenticateAsync_WithValidApiKey_ReturnsPrincipalWithScopes()
    {
        var handler = CreateHandler(new AbacControllerOptions
        {
            Auth = new AuthOptions
            {
                ApiKeys =
                [
                    new ApiKeyConfig
                    {
                        Key = "test-key",
                        ClientId = "svc-a",
                        Scopes = ["abac:evaluate", "abac:policy:read"]
                    }
                ]
            }
        }, headers => headers[ApiKeyAuthenticationDefaults.HeaderName] = "test-key");

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("svc-a", result.Principal!.FindFirst("sub")!.Value);
        Assert.Equal("svc-a", result.Principal.FindFirst("client_id")!.Value);
        Assert.Equal("abac:evaluate abac:policy:read", result.Principal.FindFirst("scope")!.Value);
    }

    [Fact]
    public async Task AuthenticateAsync_WithMissingApiKey_ReturnsNoResult()
    {
        var handler = CreateHandler(new AbacControllerOptions(), _ => { });

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.True(result.None);
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidApiKey_Fails()
    {
        var handler = CreateHandler(new AbacControllerOptions
        {
            Auth = new AuthOptions
            {
                ApiKeys =
                [
                    new ApiKeyConfig
                    {
                        Key = "expected-key",
                        ClientId = "svc-a"
                    }
                ]
            }
        }, headers => headers[ApiKeyAuthenticationDefaults.HeaderName] = "wrong-key");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.False(result.None);
        Assert.Equal("Invalid API key", result.Failure!.Message);
    }

    private static ApiKeyAuthHandler CreateHandler(AbacControllerOptions options, Action<IHeaderDictionary> configureHeaders)
    {
        var handler = new ApiKeyAuthHandler(
            new TestOptionsMonitor(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            options);

        var httpContext = new DefaultHttpContext();
        configureHeaders(httpContext.Request.Headers);

        var scheme = new AuthenticationScheme(
            ApiKeyAuthenticationDefaults.SchemeName,
            ApiKeyAuthenticationDefaults.SchemeName,
            typeof(ApiKeyAuthHandler));

        handler.InitializeAsync(scheme, httpContext).GetAwaiter().GetResult();
        return handler;
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();

        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }
}
