using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Tests.Integration.Fixtures;

namespace AbacController.Tests.Integration.Docker;

[Collection("Docker")]
public sealed class OpenApiTests
{
    private readonly HttpClient _http;

    public OpenApiTests(AbacContainerFixture fixture)
    {
        _http = fixture.HttpClient;
    }

    [Fact]
    public async Task SwaggerJson_IsServedWithExpectedMetadata()
    {
        var response = await _http.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(doc);

        var root = doc.RootElement;
        Assert.Equal("3.0.1", root.GetProperty("openapi").GetString());

        var info = root.GetProperty("info");
        Assert.Equal("ABAC Controller API", info.GetProperty("title").GetString());
        Assert.Equal("v1", info.GetProperty("version").GetString());

        var paths = root.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/access/v1/evaluation", out _));
        Assert.True(paths.TryGetProperty("/.well-known/authzen-configuration", out _));
        Assert.True(paths.TryGetProperty("/api/v1/pap/policy-sets", out _));
    }

}
