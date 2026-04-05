using System.Net;
using System.Net.Http.Json;
using AbacController.Tests.Integration.Fixtures;

namespace AbacController.Tests.Integration.Docker;

public sealed class AuthenticationScenarioTests
{
    [Fact]
    public async Task ApiKeyOnlyMode_RejectsMissingAndInvalidKeys_AndAcceptsValidKey()
    {
        await using var container = new ConfigurableAbacContainer(new Dictionary<string, string>
        {
            ["ABAC_Auth__ApiKeys__0__Key"] = "integration-secret",
            ["ABAC_Auth__ApiKeys__0__ClientId"] = "integration-client",
            ["ABAC_Auth__ApiKeys__0__Scopes__0"] = "abac:evaluate"
        });
        await container.StartAsync();

        var payload = new
        {
            requestId = "apikey-1",
            subject = new { type = "user", id = "alice", properties = new { } },
            action = new { name = "read", properties = new { } },
            resource = new { type = "document", id = "doc-1", properties = new { } }
        };

        var noKey = await container.HttpClient.PostAsJsonAsync("/access/v1/evaluation", payload);
        Assert.Equal(HttpStatusCode.Unauthorized, noKey.StatusCode);

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/access/v1/evaluation")
        {
            Content = JsonContent.Create(payload)
        };
        invalidRequest.Headers.TryAddWithoutValidation("X-API-Key", "wrong-secret");
        using var invalidKey = await container.HttpClient.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidKey.StatusCode);

        using var validRequest = new HttpRequestMessage(HttpMethod.Post, "/access/v1/evaluation")
        {
            Content = JsonContent.Create(payload)
        };
        validRequest.Headers.TryAddWithoutValidation("X-API-Key", "integration-secret");
        using var validKey = await container.HttpClient.SendAsync(validRequest);
        Assert.Equal(HttpStatusCode.OK, validKey.StatusCode);
    }
}
