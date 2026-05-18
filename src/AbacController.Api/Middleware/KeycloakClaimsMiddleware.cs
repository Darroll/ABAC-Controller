using AbacController.Api.Auth;

namespace AbacController.Api.Middleware;

/// <summary>
/// Pipeline middleware that, after the authentication step, scrapes the
/// authenticated principal for Keycloak group claims and stashes them in a
/// scoped <see cref="KeycloakClaimsContext"/>. Downstream services then read
/// the context instead of poking at <see cref="System.Security.Claims.ClaimsPrincipal"/>
/// directly, which keeps the membership resolver framework-agnostic.
///
/// No-op when the principal isn't authenticated or has no group claims.
/// </summary>
public sealed class KeycloakClaimsMiddleware
{
    private readonly RequestDelegate _next;

    public KeycloakClaimsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IKeycloakClaimsContext claimsContext)
    {
        if (claimsContext is KeycloakClaimsContext mutable && context.User?.Identity?.IsAuthenticated == true)
        {
            mutable.SetGroups(KeycloakClaimsContext.ExtractGroups(context.User));
        }

        await _next(context);
    }
}
