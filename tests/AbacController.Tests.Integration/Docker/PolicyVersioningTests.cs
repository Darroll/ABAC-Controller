using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Tests.Integration.Fixtures;

namespace AbacController.Tests.Integration.Docker;

[Collection("Docker")]
public sealed class PolicyVersioningTests
{
    private readonly HttpClient _http;

    public PolicyVersioningTests(AbacContainerFixture fixture)
    {
        _http = fixture.HttpClient;
    }

    [Fact]
    public async Task DiffAndRollback_WorkEndToEnd()
    {
        var policySetId = $"ps-{Guid.NewGuid():N}";
        var policyId = $"p-{Guid.NewGuid():N}";

        await CreatePolicySetAsync(policySetId);
        await CreatePolicyAsync(policyId, policySetId);

        var v1 = await CreateVersionAsync(policyId, "Permit", "v1");
        var v2 = await CreateVersionAsync(policyId, "Deny", "v2");

        var diffResponse = await _http.GetAsync($"/pap/api/policies/{policyId}/versions/{v1.GetProperty("id").GetGuid()}/diff/{v2.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.OK, diffResponse.StatusCode);
        var diff = await diffResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(diff);
        Assert.True(diff.RootElement.GetProperty("hasChanges").GetBoolean());

        var rollbackResponse = await _http.PostAsJsonAsync(
            $"/pap/api/policies/{policyId}/versions/{v1.GetProperty("id").GetGuid()}/rollback",
            new { createdBy = "docker-test", reason = "restore permit policy" });
        Assert.Equal(HttpStatusCode.OK, rollbackResponse.StatusCode);
        var rollbackVersion = await rollbackResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(rollbackVersion);
        Assert.True(rollbackVersion.RootElement.GetProperty("isActive").GetBoolean());
        Assert.Equal(3, rollbackVersion.RootElement.GetProperty("versionNumber").GetInt32());

        var versionsResponse = await _http.GetAsync($"/pap/api/policies/{policyId}/versions");
        Assert.Equal(HttpStatusCode.OK, versionsResponse.StatusCode);
        var versions = await versionsResponse.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(versions);
        Assert.Equal(3, versions.Length);
        Assert.Single(versions, v => v.GetProperty("isActive").GetBoolean());
        Assert.Equal(3, versions.Max(v => v.GetProperty("versionNumber").GetInt32()));
    }

    private async Task CreatePolicySetAsync(string policySetId)
    {
        var response = await _http.PutAsJsonAsync($"/pap/api/policy-sets/{policySetId}", new
        {
            id = policySetId,
            name = "Versioning Test Policy Set",
            combiningAlgorithm = "deny-overrides",
            isActive = true,
            policies = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task CreatePolicyAsync(string policyId, string policySetId)
    {
        var response = await _http.PutAsJsonAsync($"/pap/api/policies/{policyId}", new
        {
            id = policyId,
            policySetId,
            name = "Versioning Test Policy",
            format = "native",
            versions = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> CreateVersionAsync(string policyId, string effect, string createdBy)
    {
        var policyDocument = JsonSerializer.Serialize(new
        {
            id = $"rule-{effect.ToLowerInvariant()}",
            effect,
            conditions = Array.Empty<object>()
        });

        var response = await _http.PostAsJsonAsync($"/pap/api/policies/{policyId}/versions", new
        {
            content = policyDocument,
            createdBy,
            activate = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var version = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(version);
        return version.RootElement.Clone();
    }
}
