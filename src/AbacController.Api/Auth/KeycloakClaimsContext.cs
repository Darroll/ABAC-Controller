using System.Security.Claims;
using AbacController.Core.Interfaces;

namespace AbacController.Api.Auth;

/// <summary>
/// API-side alias for <see cref="IKeycloakGroupClaimsProvider"/>. Lives here
/// so the middleware code can keep the API-friendly name; engines under
/// <c>AbacController.Pdp</c> consume the Core interface directly so they
/// don't pull a dependency on the API project.
/// </summary>
public interface IKeycloakClaimsContext : IKeycloakGroupClaimsProvider
{
}

/// <summary>
/// Default scoped implementation of <see cref="IKeycloakClaimsContext"/>.
/// Mutated only by <see cref="KeycloakClaimsMiddleware"/>; everything else
/// reads it.
/// </summary>
public sealed class KeycloakClaimsContext : IKeycloakClaimsContext
{
    private static readonly IReadOnlyCollection<string> Empty = Array.Empty<string>();

    /// <inheritdoc />
    public IReadOnlyCollection<string> KeycloakGroupIds { get; private set; } = Empty;

    /// <summary>
    /// Replaces the current group set. Internal-by-convention — only the
    /// middleware should call this; everything else reads
    /// <see cref="KeycloakGroupIds"/>.
    /// </summary>
    public void SetGroups(IReadOnlyCollection<string> groups)
    {
        KeycloakGroupIds = groups ?? Empty;
    }

    /// <summary>
    /// Reads the <c>groups</c> and <c>memberOf</c> claims from a principal,
    /// dedupes, and returns the union. Public so the middleware tests can
    /// exercise it without instantiating a full HTTP context.
    /// </summary>
    public static IReadOnlyCollection<string> ExtractGroups(ClaimsPrincipal principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Empty;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in principal.FindAll("groups").Concat(principal.FindAll("memberOf")))
        {
            if (!string.IsNullOrWhiteSpace(claim.Value))
            {
                seen.Add(claim.Value);
            }
        }
        return seen;
    }
}
