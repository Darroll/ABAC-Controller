// <copyright file="BlazorAdminAuthForwardingHandler.cs" company="ABAC Controller">
// Copyright (c) ABAC Controller. All rights reserved.
// </copyright>

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace AbacController.Api.Hosting;

/// <summary>
/// Forwards the current Blazor circuit's <c>Authorization</c> header and
/// <c>X-Tenant-Id</c> onto outgoing in-process PAP / PDP calls so the
/// embedded admin UI runs against the API with the signed-in admin's
/// scopes and the active tenant context — not a hard-coded loopback.
/// </summary>
/// <remarks>
/// Registered as a <see cref="DelegatingHandler"/> on the named
/// <c>BlazorAdmin</c> HttpClient in <see cref="ServiceCollectionExtensions"/>.
/// Runs per-request so it picks up the current circuit's state rather than
/// a cached value.
/// </remarks>
public sealed class BlazorAdminAuthForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initializes a new instance of the <see cref="BlazorAdminAuthForwardingHandler"/> class.</summary>
    public BlazorAdminAuthForwardingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var incoming = _httpContextAccessor.HttpContext?.Request;
        if (incoming is not null)
        {
            // Forward Authorization (bearer token, API key, etc.) so the
            // PAP call runs under the same identity as the admin request.
            if (request.Headers.Authorization is null &&
                incoming.Headers.TryGetValue("Authorization", out var auth))
            {
                var value = auth.ToString();
                if (!string.IsNullOrWhiteSpace(value) &&
                    AuthenticationHeaderValue.TryParse(value, out var parsed))
                {
                    request.Headers.Authorization = parsed;
                }
            }

            // Forward the tenant header. The Razor pages may also set
            // X-Tenant-Id explicitly on a per-request basis — if they
            // did, respect that choice and skip.
            if (!request.Headers.Contains("X-Tenant-Id") &&
                incoming.Headers.TryGetValue("X-Tenant-Id", out var tenant))
            {
                var value = tenant.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    request.Headers.Add("X-Tenant-Id", value);
                }
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
