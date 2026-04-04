using AbacController.Core.Interfaces;

namespace AbacController.Api.Middleware;

/// <summary>
/// Middleware that resolves the current tenant from the request.
/// Resolution order: X-Tenant-Id header → tenant_id JWT claim → null (default tenant).
/// </summary>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Initializes a new instance of the <see cref="TenantContextMiddleware"/> class.</summary>
    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Resolves the tenant and stores it in <see cref="HttpTenantContext"/>.</summary>
    public async Task InvokeAsync(HttpContext httpContext, HttpTenantContext tenantContext)
    {
        // 1. Check X-Tenant-Id header
        var tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        // 2. Fall back to JWT claim
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = httpContext.User.FindFirst("tenant_id")?.Value;
        }

        // Normalize: empty/whitespace → null
        tenantContext.TenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim();

        await _next(httpContext);
    }
}

/// <summary>
/// Scoped tenant context populated per-request by <see cref="TenantContextMiddleware"/>.
/// </summary>
public sealed class HttpTenantContext : ITenantContext
{
    /// <inheritdoc />
    public string? TenantId { get; set; }
}
