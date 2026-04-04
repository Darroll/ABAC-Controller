using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace AbacController.Blazor;

/// <summary>
/// Delegating handler that forwards inbound authentication and tenant-routing headers
/// from the current Blazor Server request to outbound ABAC API calls.
/// </summary>
public sealed class ApiAuthenticationForwardingHandler : DelegatingHandler
{
    private const string AuthorizationHeader = "Authorization";
    private const string TenantHeader = "X-Tenant-Id";
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiAuthenticationForwardingHandler"/> class.
    /// </summary>
    public ApiAuthenticationForwardingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Copies the current request's authorization and tenant headers to the outbound API request.
    /// </summary>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            if (!request.Headers.Contains(AuthorizationHeader) &&
                httpContext.Request.Headers.TryGetValue(AuthorizationHeader, out var authorizationValues) &&
                AuthenticationHeaderValue.TryParse(authorizationValues.ToString(), out var authHeader))
            {
                request.Headers.Authorization = authHeader;
            }

            if (!request.Headers.Contains(TenantHeader) &&
                httpContext.Request.Headers.TryGetValue(TenantHeader, out var tenantValues))
            {
                request.Headers.TryAddWithoutValidation(TenantHeader, tenantValues.ToArray());
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
