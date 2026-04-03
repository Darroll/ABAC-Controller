using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace AbacController.Api.Auth;

/// <summary>
/// Requires the caller to present a specific OAuth scope.
/// Supports both space-delimited <c>scope</c> claims and repeated/scalar <c>scp</c> claims.
/// </summary>
public sealed class ScopeAuthorizationRequirement(string requiredScope) : IAuthorizationRequirement
{
    public string RequiredScope { get; } = requiredScope;
}

public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeAuthorizationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeAuthorizationRequirement requirement)
    {
        foreach (var claim in context.User.FindAll("scope").Concat(context.User.FindAll("scp")))
        {
            var scopes = claim.Value
                .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (scopes.Contains(requirement.RequiredScope, StringComparer.Ordinal))
            {
                context.Succeed(requirement);
                break;
            }
        }

        return Task.CompletedTask;
    }
}

public static class AuthorizationPolicyBuilderExtensions
{
    public static AuthorizationPolicyBuilder RequireScope(this AuthorizationPolicyBuilder builder, string scope)
    {
        builder.RequireAuthenticatedUser();
        builder.AddRequirements(new ScopeAuthorizationRequirement(scope));
        return builder;
    }
}
