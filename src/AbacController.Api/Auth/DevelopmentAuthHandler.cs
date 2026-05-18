using System.Security.Claims;
using AbacController.Core.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AbacController.Api.Auth;

/// <summary>Default values for development authentication.</summary>
public static class DevelopmentAuthenticationDefaults
{
    /// <summary>Authentication scheme name.</summary>
    public const string SchemeName = "Development";
}

/// <summary>
/// Explicitly opt-in development authentication handler.
/// Never enabled automatically.
/// </summary>
public sealed class DevelopmentAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Initializes a new instance of the <see cref="DevelopmentAuthHandler"/> class.</summary>
    public DevelopmentAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(DevelopmentAuthenticationDefaults.SchemeName);
        identity.AddClaim(new Claim("sub", "dev-user"));
        identity.AddClaim(new Claim("scope", string.Join(' ',
            Scopes.Evaluate,
            Scopes.EvaluateExplain,
            Scopes.PolicyRead,
            Scopes.PolicyWrite,
            Scopes.PolicyAdmin,
            Scopes.PipRead,
            Scopes.PipAdmin,
            Scopes.PepRead,
            Scopes.PepAdmin,
            Scopes.PepLabel,
            Scopes.AuditRead,
            Scopes.SysRead,
            Scopes.SysAdmin,
            Scopes.ClassificationQuery,
            Scopes.ApplicationAdmin,
            Scopes.EntitlementAdmin,
            Scopes.RecipientCheck,
            Scopes.WebhookAdmin,
            Scopes.AuditMirrorWrite,
            Scopes.GroupAdmin)));

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, DevelopmentAuthenticationDefaults.SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
