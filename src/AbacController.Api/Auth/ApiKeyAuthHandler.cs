using System.Security.Claims;
using System.Text.Encodings.Web;
using AbacController.Api.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AbacController.Api.Auth;

public static class ApiKeyAuthenticationDefaults
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";
}

/// <summary>
/// API key authentication handler for service-to-service calls.
/// Validates API keys from configuration against the X-API-Key header.
/// </summary>
public sealed class ApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AbacControllerOptions _options;

    public ApiKeyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AbacControllerOptions abacOptions)
        : base(options, logger, encoder)
    {
        _options = abacOptions;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var apiKeyValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var apiKey = apiKeyValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var matchedKey = _options.Auth.ApiKeys
            .FirstOrDefault(k => string.Equals(k.Key, apiKey, StringComparison.Ordinal));

        if (matchedKey is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var identity = new ClaimsIdentity(ApiKeyAuthenticationDefaults.SchemeName);
        identity.AddClaim(new Claim("sub", matchedKey.ClientId));
        identity.AddClaim(new Claim("client_id", matchedKey.ClientId));

        if (matchedKey.Scopes.Count > 0)
        {
            identity.AddClaim(new Claim("scope", string.Join(' ', matchedKey.Scopes)));
        }

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationDefaults.SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
