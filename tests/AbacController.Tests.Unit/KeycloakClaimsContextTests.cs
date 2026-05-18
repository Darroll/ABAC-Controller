using System.Security.Claims;
using AbacController.Api.Auth;
using AbacController.Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for the Keycloak claims context, the middleware that populates it,
/// and the realm-as-tenant fallback in <see cref="TenantContextMiddleware"/>.
/// </summary>
public sealed class KeycloakClaimsContextTests
{
    [Fact]
    public void ExtractGroups_AnonymousPrincipal_ReturnsEmpty()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var groups = KeycloakClaimsContext.ExtractGroups(principal);
        Assert.Empty(groups);
    }

    [Fact]
    public void ExtractGroups_GroupsClaim_ReturnsAllValues()
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("groups", "nato"));
        identity.AddClaim(new Claim("groups", "ops"));
        var principal = new ClaimsPrincipal(identity);

        var groups = KeycloakClaimsContext.ExtractGroups(principal);

        Assert.Equal(2, groups.Count);
        Assert.Contains("nato", groups);
        Assert.Contains("ops", groups);
    }

    [Fact]
    public void ExtractGroups_AlsoReadsMemberOf_AndDedupes()
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("groups", "nato"));
        identity.AddClaim(new Claim("memberOf", "nato")); // dup
        identity.AddClaim(new Claim("memberOf", "ldap-ops"));
        var principal = new ClaimsPrincipal(identity);

        var groups = KeycloakClaimsContext.ExtractGroups(principal);

        Assert.Equal(2, groups.Count);
        Assert.Contains("nato", groups);
        Assert.Contains("ldap-ops", groups);
    }

    [Fact]
    public async Task Middleware_PopulatesContext_WhenAuthenticated()
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("groups", "nato"));
        context.User = new ClaimsPrincipal(identity);

        var claimsContext = new KeycloakClaimsContext();
        var nextCalled = false;
        var middleware = new KeycloakClaimsMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, claimsContext);

        Assert.True(nextCalled);
        Assert.Single(claimsContext.KeycloakGroupIds);
        Assert.Contains("nato", claimsContext.KeycloakGroupIds);
    }

    [Fact]
    public async Task Middleware_LeavesContextEmpty_WhenAnonymous()
    {
        var context = new DefaultHttpContext(); // anonymous principal
        var claimsContext = new KeycloakClaimsContext();
        var middleware = new KeycloakClaimsMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, claimsContext);

        Assert.Empty(claimsContext.KeycloakGroupIds);
    }

    [Fact]
    public void TryExtractRealmFromIssuer_ParsesKeycloakUrl()
    {
        Assert.Equal("abac-dev",
            TenantContextMiddleware.TryExtractRealmFromIssuer("http://keycloak:8080/realms/abac-dev"));
        Assert.Equal("abac-dev",
            TenantContextMiddleware.TryExtractRealmFromIssuer("http://keycloak/realms/abac-dev/"));
        Assert.Equal("abac-dev",
            TenantContextMiddleware.TryExtractRealmFromIssuer("http://keycloak/realms/abac-dev/protocol/openid-connect"));
    }

    [Fact]
    public void TryExtractRealmFromIssuer_ReturnsNull_WhenNoRealmsSegment()
    {
        Assert.Null(TenantContextMiddleware.TryExtractRealmFromIssuer("https://login.microsoftonline.com/tenant/v2.0"));
        Assert.Null(TenantContextMiddleware.TryExtractRealmFromIssuer(""));
        Assert.Null(TenantContextMiddleware.TryExtractRealmFromIssuer(null));
    }

    [Fact]
    public async Task TenantContextMiddleware_FallsBackToRealmFromIss()
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("iss", "http://keycloak:8080/realms/abac-dev"));
        context.User = new ClaimsPrincipal(identity);

        var tenant = new HttpTenantContext();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, tenant);

        Assert.Equal("abac-dev", tenant.TenantId);
    }

    [Fact]
    public async Task TenantContextMiddleware_PrefersHeaderOverIss()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = "tenant-from-header";
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("iss", "http://keycloak:8080/realms/abac-dev"));
        context.User = new ClaimsPrincipal(identity);

        var tenant = new HttpTenantContext();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, tenant);

        Assert.Equal("tenant-from-header", tenant.TenantId);
    }

    [Fact]
    public async Task TenantContextMiddleware_PrefersExplicitTenantClaimOverIss()
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("tenant_id", "explicit-tenant"));
        identity.AddClaim(new Claim("iss", "http://keycloak:8080/realms/abac-dev"));
        context.User = new ClaimsPrincipal(identity);

        var tenant = new HttpTenantContext();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, tenant);

        Assert.Equal("explicit-tenant", tenant.TenantId);
    }
}
