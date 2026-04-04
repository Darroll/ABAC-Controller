using System.Net;
using System.Text.Json;
using AbacController.Core.Domain.Attributes;
using AbacController.Pip.Sources;

namespace AbacController.Tests.Unit;

public sealed class OidcPipSourceTests
{
    [Fact]
    public async Task ResolveAsync_FetchesAttributesFromUserInfo()
    {
        var userInfoResponse = JsonSerializer.Serialize(new
        {
            sub = "alice",
            email = "alice@example.com",
            groups = new[] { "engineering", "admin" },
            department = "ENG",
            clearance_level = 3
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, userInfoResponse);
        var httpClient = new HttpClient(handler);

        var claimMapping = new Dictionary<string, string>
        {
            ["email"] = "email",
            ["department"] = "department",
            ["clearance_level"] = "clearanceLevel",
            ["groups"] = "groups"
        };

        var source = new OidcPipSource(
            "oidc-test",
            5,
            "https://idp.example.com/userinfo",
            claimMapping,
            httpClient);

        var result = await source.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "alice",
            SubjectType = "user",
            RequestedAttributes = ["email", "department", "clearanceLevel", "groups"],
            Context = new Dictionary<string, object?> { ["bearerToken"] = "test-token-123" }
        });

        Assert.True(result.Success);
        Assert.Equal(4, result.Values.Count);
        Assert.Equal("alice@example.com", result.Values.First(v => v.Name == "email").Value);
        Assert.Equal("ENG", result.Values.First(v => v.Name == "department").Value);
        Assert.Equal(3L, result.Values.First(v => v.Name == "clearanceLevel").Value);
    }

    [Fact]
    public async Task ResolveAsync_FailsWithoutBearerToken()
    {
        var httpClient = new HttpClient(new FakeHttpHandler(HttpStatusCode.OK, "{}"));
        var source = new OidcPipSource(
            "oidc-test", 5,
            "https://idp.example.com/userinfo",
            new Dictionary<string, string> { ["email"] = "email" },
            httpClient);

        var result = await source.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "alice",
            SubjectType = "user",
            RequestedAttributes = ["email"],
            Context = new Dictionary<string, object?>()
        });

        Assert.False(result.Success);
        Assert.Contains("bearer token", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_HandlesUnauthorizedResponse()
    {
        var httpClient = new HttpClient(new FakeHttpHandler(HttpStatusCode.Unauthorized, ""));
        var source = new OidcPipSource(
            "oidc-test", 5,
            "https://idp.example.com/userinfo",
            new Dictionary<string, string> { ["email"] = "email" },
            httpClient);

        var result = await source.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "alice",
            SubjectType = "user",
            RequestedAttributes = ["email"],
            Context = new Dictionary<string, object?> { ["bearerToken"] = "expired-token" }
        });

        Assert.False(result.Success);
        Assert.Contains("Unauthorized", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProvidesAttributes_ReflectsClaimMapping()
    {
        var httpClient = new HttpClient(new FakeHttpHandler(HttpStatusCode.OK, "{}"));
        var source = new OidcPipSource(
            "oidc-test", 5,
            "https://idp.example.com/userinfo",
            new Dictionary<string, string>
            {
                ["email"] = "email",
                ["department"] = "department",
                ["clearance_level"] = "clearanceLevel"
            },
            httpClient);

        Assert.Contains("email", source.ProvidesAttributes);
        Assert.Contains("department", source.ProvidesAttributes);
        Assert.Contains("clearanceLevel", source.ProvidesAttributes);
    }

    [Fact]
    public async Task TestConnectivity_ReturnsHealthyForReachableEndpoint()
    {
        var httpClient = new HttpClient(new FakeHttpHandler(HttpStatusCode.Unauthorized, ""));
        var source = new OidcPipSource(
            "oidc-test", 5,
            "https://idp.example.com/userinfo",
            new Dictionary<string, string>(),
            httpClient);

        var health = await source.TestConnectivityAsync();
        Assert.True(health.Healthy); // Even 401 means endpoint is reachable
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public FakeHttpHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
