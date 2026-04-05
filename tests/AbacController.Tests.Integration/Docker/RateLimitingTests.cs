using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Tests.Integration.Fixtures;

namespace AbacController.Tests.Integration.Docker;

public sealed class RateLimitingTests
{
    [Fact]
    public async Task EvaluateEndpoint_ReturnsTooManyRequests_WhenGlobalLimitExceeded()
    {
        await using var container = new ConfigurableAbacContainer(new Dictionary<string, string>
        {
            ["ABAC_Auth__EnableDevelopmentAuth"] = "true",
            ["ABAC_RateLimiting__PdpPermitLimit"] = "2",
            ["ABAC_RateLimiting__WindowSeconds"] = "60",
            ["ABAC_RateLimiting__PerClientPermitLimit"] = "0",
            ["ABAC_RateLimiting__PerResourcePermitLimit"] = "0"
        });
        await container.StartAsync();

        var configResponse = await container.HttpClient.GetAsync("/system/api/config");
        configResponse.EnsureSuccessStatusCode();
        var config = await configResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, config.GetProperty("rateLimit").GetProperty("pdpPermitLimit").GetInt32());

        var requests = Enumerable.Range(0, 10)
            .Select(i => container.HttpClient.PostAsJsonAsync("/access/v1/evaluation", new
            {
                requestId = $"rate-limit-{i}",
                subject = new { type = "user", id = "alice", properties = new { } },
                action = new { name = "read", properties = new { } },
                resource = new { type = "document", id = $"doc-{i}", properties = new { } }
            }))
            .ToArray();

        var responses = await Task.WhenAll(requests);

        Assert.Contains(responses, static response => response.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.Contains(responses, static response => response.StatusCode == HttpStatusCode.OK);
    }
}
