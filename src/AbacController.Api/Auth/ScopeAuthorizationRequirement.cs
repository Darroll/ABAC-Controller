using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace AbacController.Api.Auth;

/// <summary>
/// Requires the caller to present a specific OAuth scope.
/// Supports both space-delimited <c>scope</c> claims and repeated/scalar <c>scp</c> claims.
/// </summary>
public sealed class ScopeAuthorizationRequirement(string requiredScope) : IAuthorizationRequirement
{
    /// <summary>The scope that must be present.</summary>
    public string RequiredScope { get; } = requiredScope;
}

/// <summary>
/// Authorization handler that checks for the required scope in the caller's claims.
/// </summary>
public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeAuthorizationRequirement>
{
    /// <inheritdoc />
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

/// <summary>Extension methods for configuring scope-based authorization policies.</summary>
public static class AuthorizationPolicyBuilderExtensions
{
    /// <summary>Adds a scope requirement to the authorization policy.</summary>
    public static AuthorizationPolicyBuilder RequireScope(this AuthorizationPolicyBuilder builder, string scope)
    {
        builder.RequireAuthenticatedUser();
        builder.AddRequirements(new ScopeAuthorizationRequirement(scope));
        return builder;
    }
}
