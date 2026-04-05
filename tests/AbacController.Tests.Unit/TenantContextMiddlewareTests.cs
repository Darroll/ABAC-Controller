using System.Security.Claims;
using AbacController.Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace AbacController.Tests.Unit;

/// <summary>
/// Unit tests for tenant resolution middleware behavior.
/// </summary>
public sealed class TenantContextMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_PrefersHeaderOverClaim()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "tenant-from-header";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("tenant_id", "tenant-from-claim")
        ]));

        var tenantContext = new HttpTenantContext();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Equal("tenant-from-header", tenantContext.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_UsesClaimWhenHeaderMissing()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("tenant_id", "tenant-from-claim")
        ]));

        var tenantContext = new HttpTenantContext();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Equal("tenant-from-claim", tenantContext.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_TrimsWhitespaceAndNormalizesEmptyToNull()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "  tenant-a  ";

        var tenantContext = new HttpTenantContext();
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Equal("tenant-a", tenantContext.TenantId);

        httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "   ";
        tenantContext = new HttpTenantContext();

        await middleware.InvokeAsync(httpContext, tenantContext);

        Assert.Null(tenantContext.TenantId);
    }
}
