using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Tests.Integration.Fixtures;

namespace AbacController.Tests.Integration.Docker;

/// <summary>
/// End-to-end tenant isolation tests against the shipped Docker image.
/// </summary>
[Collection("Docker")]
public sealed class MultiTenantIsolationTests
{
    private readonly AbacContainerFixture _fixture;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiTenantIsolationTests"/> class.
    /// </summary>
    public MultiTenantIsolationTests(AbacContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PolicySetListing_IsScopedByTenantHeader()
    {
        await PutPolicySetAsync("tenant-a", "tenant-a-ps", "Tenant A Policy Set");
        await PutPolicySetAsync("tenant-b", "tenant-b-ps", "Tenant B Policy Set");
        await PutPolicySetAsync(null, "default-ps", "Default Policy Set");

        var tenantAItems = await GetPolicySetIdsAsync("tenant-a");
        var tenantBItems = await GetPolicySetIdsAsync("tenant-b");
        var defaultItems = await GetPolicySetIdsAsync(null);

        Assert.Contains("tenant-a-ps", tenantAItems);
        Assert.DoesNotContain("tenant-b-ps", tenantAItems);
        Assert.DoesNotContain("default-ps", tenantAItems);

        Assert.Contains("tenant-b-ps", tenantBItems);
        Assert.DoesNotContain("tenant-a-ps", tenantBItems);
        Assert.DoesNotContain("default-ps", tenantBItems);

        Assert.Contains("default-ps", defaultItems);
        Assert.DoesNotContain("tenant-a-ps", defaultItems);
        Assert.DoesNotContain("tenant-b-ps", defaultItems);
    }

    private async Task PutPolicySetAsync(string? tenantId, string id, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/pap/api/policy-sets/{id}")
        {
            Content = JsonContent.Create(new
            {
                id,
                name,
                combiningAlgorithm = "deny-overrides",
                isActive = true
            })
        };

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            request.Headers.Add("X-Tenant-Id", tenantId);
        }

        using var response = await _fixture.HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task<List<string>> GetPolicySetIdsAsync(string? tenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/pap/api/policy-sets");
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            request.Headers.Add("X-Tenant-Id", tenantId);
        }

        using var response = await _fixture.HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.EnumerateArray()
            .Select(static item => item.GetProperty("id").GetString()!)
            .ToList();
    }
}
