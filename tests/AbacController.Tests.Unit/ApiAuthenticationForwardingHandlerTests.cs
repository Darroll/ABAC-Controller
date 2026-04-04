using System.Net;
using AbacController.Blazor;
using Microsoft.AspNetCore.Http;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests outbound auth and tenant header forwarding from Blazor to the ABAC API.
/// </summary>
public sealed class ApiAuthenticationForwardingHandlerTests
{
    [Fact]
    public async Task SendAsync_ForwardsAuthorizationAndTenantHeaders()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer test-token";
        httpContext.Request.Headers["X-Tenant-Id"] = "tenant-a";

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var inner = new CaptureHandler();
        var handler = new ApiAuthenticationForwardingHandler(accessor)
        {
            InnerHandler = inner
        };

        using var client = new HttpClient(handler);
        using var response = await client.GetAsync("http://example.test/api");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer", inner.CapturedRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("test-token", inner.CapturedRequest.Headers.Authorization.Parameter);
        Assert.Equal("tenant-a", inner.CapturedRequest.Headers.GetValues("X-Tenant-Id").Single());
    }

    [Fact]
    public async Task SendAsync_DoesNotOverwriteExistingRequestHeaders()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer inbound-token";
        httpContext.Request.Headers["X-Tenant-Id"] = "tenant-inbound";

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var inner = new CaptureHandler();
        var handler = new ApiAuthenticationForwardingHandler(accessor)
        {
            InnerHandler = inner
        };

        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/api");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "explicit-token");
        request.Headers.TryAddWithoutValidation("X-Tenant-Id", "tenant-explicit");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("explicit-token", inner.CapturedRequest!.Headers.Authorization!.Parameter);
        Assert.Equal("tenant-explicit", inner.CapturedRequest.Headers.GetValues("X-Tenant-Id").Single());
    }

    [Fact]
    public async Task SendAsync_WithoutHttpContext_LeavesHeadersUnset()
    {
        var accessor = new HttpContextAccessor();
        var inner = new CaptureHandler();
        var handler = new ApiAuthenticationForwardingHandler(accessor)
        {
            InnerHandler = inner
        };

        using var client = new HttpClient(handler);
        using var response = await client.GetAsync("http://example.test/api");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(inner.CapturedRequest!.Headers.Authorization);
        Assert.False(inner.CapturedRequest.Headers.Contains("X-Tenant-Id"));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
