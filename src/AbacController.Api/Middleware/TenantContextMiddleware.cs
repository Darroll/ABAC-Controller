using AbacController.Core.Interfaces;

namespace AbacController.Api.Middleware;

/// <summary>
/// Middleware that resolves the current tenant from the request.
/// Resolution order:
/// 1. <c>X-Tenant-Id</c> header.
/// 2. <c>tenant_id</c> JWT claim (Keycloak custom claim mapper, Entra extension, etc.).
/// 3. Realm name parsed from the token's <c>iss</c> claim
///    (<c>.../realms/{tenantId}</c>) — convention used by the dev Keycloak setup.
/// 4. null → default tenant.
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

        // 2. Fall back to explicit tenant_id JWT claim
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = httpContext.User.FindFirst("tenant_id")?.Value;
        }

        // 3. Fall back to the realm name parsed from the iss claim. Keycloak
        //    issuer URLs are of the form <base>/realms/<realm>; we treat the
        //    realm name as the ABAC tenant id by convention so a single dev
        //    realm "abac-dev" maps cleanly to the "abac-dev" tenant. Production
        //    deployments using a single realm for multiple tenants should
        //    configure a custom claim mapper instead.
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            var iss = httpContext.User.FindFirst("iss")?.Value;
            tenantId = TryExtractRealmFromIssuer(iss);
        }

        // Normalize: empty/whitespace → null
        tenantContext.TenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim();

        await _next(httpContext);
    }

    /// <summary>
    /// Extracts the realm name from a Keycloak-style issuer URL. Returns null
    /// when the URL doesn't follow the convention. Public so tests can exercise
    /// the parser without spinning up an HTTP context.
    /// </summary>
    public static string? TryExtractRealmFromIssuer(string? issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer)) return null;

        // Find "/realms/" and take the segment immediately after it.
        const string marker = "/realms/";
        var idx = issuer.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var start = idx + marker.Length;
        if (start >= issuer.Length) return null;

        var rest = issuer.AsSpan(start);
        var slash = rest.IndexOf('/');
        var realm = (slash < 0 ? rest : rest[..slash]).ToString();
        return string.IsNullOrWhiteSpace(realm) ? null : realm;
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
