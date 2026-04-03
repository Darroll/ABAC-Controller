using System.Security.Claims;
using AbacController.Core.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AbacController.Api.Auth;

public static class DevelopmentAuthenticationDefaults
{
    public const string SchemeName = "Development";
}

/// <summary>
/// Explicitly opt-in development authentication handler.
/// Never enabled automatically.
/// </summary>
public sealed class DevelopmentAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DevelopmentAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

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
            Scopes.SysAdmin)));

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, DevelopmentAuthenticationDefaults.SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
