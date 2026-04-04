using System.Net;
using AbacController.Tests.Integration.Fixtures;

namespace AbacController.Tests.Integration.Docker;

[Collection("Docker")]
public sealed class HealthCheckTests
{
    private readonly HttpClient _http;

    public HealthCheckTests(AbacContainerFixture fixture)
    {
        _http = fixture.HttpClient;
    }

    [Fact]
    public async Task LivenessEndpoint_ReturnsOk()
    {
        var response = await _http.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadinessEndpoint_ReturnsOk()
    {
        var response = await _http.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StartupEndpoint_ReturnsOk()
    {
        var response = await _http.GetAsync("/health/startup");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MetricsEndpoint_ReturnsPlainText()
    {
        var response = await _http.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("text/plain", contentType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("abac_", body); // metrics should contain abac_ prefixed metrics
    }
}
