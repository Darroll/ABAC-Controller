using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Tests.Integration.Fixtures;

namespace AbacController.Tests.Integration.Docker;

[Collection("Docker")]
public sealed class AuthZenDiscoveryTests
{
    private readonly HttpClient _http;

    public AuthZenDiscoveryTests(AbacContainerFixture fixture)
    {
        _http = fixture.HttpClient;
    }

    [Fact]
    public async Task WellKnownConfiguration_ReturnsExpectedEndpoints()
    {
        var response = await _http.GetAsync("/.well-known/authzen-configuration");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(doc);

        var root = doc.RootElement;

        Assert.Equal("/access/v1/evaluation", root.GetProperty("evaluation_endpoint").GetString());
        Assert.Equal("/access/v1/evaluations", root.GetProperty("evaluations_endpoint").GetString());
        Assert.Equal("/access/v1/subjects", root.GetProperty("subjects_endpoint").GetString());
        Assert.Equal("/access/v1/resources", root.GetProperty("resources_endpoint").GetString());
        Assert.Equal("/access/v1/actions", root.GetProperty("actions_endpoint").GetString());
        Assert.Equal("1.0", root.GetProperty("api_version").GetString());

        // Authentication methods should include bearer
        var authMethods = root.GetProperty("authentication_methods");
        Assert.True(authMethods.GetArrayLength() > 0);
        Assert.Contains("bearer", authMethods.EnumerateArray().Select(e => e.GetString()!));

        // Issuer should be the container URL
        var issuer = root.GetProperty("issuer").GetString();
        Assert.NotNull(issuer);
        Assert.StartsWith("http://", issuer);
    }
}
