namespace AbacController.Core.Interfaces;

/// <summary>
/// Per-request provider for Keycloak group ids parsed out of the inbound JWT.
/// Lives in <c>AbacController.Core</c> so engines under <c>AbacController.Pdp</c>
/// can read it without depending on the API project. The Api project supplies
/// the actual implementation via a middleware that scrapes
/// <c>HttpContext.User</c>.
/// </summary>
public interface IKeycloakGroupClaimsProvider
{
    /// <summary>
    /// Group ids the current subject is a member of in Keycloak. Empty when
    /// the request is anonymous, has no <c>groups</c> / <c>memberOf</c> claim,
    /// or is authenticated by a non-JWT scheme.
    /// </summary>
    IReadOnlyCollection<string> KeycloakGroupIds { get; }
}
