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

    [Fact]
    public async Task SpifListing_IsScopedByTenantHeader()
    {
        var tenantAImport = $"tenant-a-import-{Guid.NewGuid():N}";
        var tenantBImport = $"tenant-b-import-{Guid.NewGuid():N}";

        await ImportSpifAsync("tenant-a", tenantAImport);
        await ImportSpifAsync("tenant-b", tenantBImport);

        var tenantASpifs = (await GetSpifsAsync("tenant-a"))
            .Where(spif => string.Equals(spif.GetProperty("importedBy").GetString(), tenantAImport, StringComparison.Ordinal))
            .ToList();
        var tenantBSpifs = (await GetSpifsAsync("tenant-b"))
            .Where(spif => string.Equals(spif.GetProperty("importedBy").GetString(), tenantBImport, StringComparison.Ordinal))
            .ToList();
        var defaultSpifs = (await GetSpifsAsync(null))
            .Where(spif => string.Equals(spif.GetProperty("importedBy").GetString(), tenantAImport, StringComparison.Ordinal)
                        || string.Equals(spif.GetProperty("importedBy").GetString(), tenantBImport, StringComparison.Ordinal))
            .ToList();

        Assert.Single(tenantASpifs);
        Assert.Single(tenantBSpifs);
        Assert.Empty(defaultSpifs);

        Assert.Equal("1.2.3.4.10", tenantASpifs[0].GetProperty("policyOid").GetString());
        Assert.Equal("1.2.3.4.20", tenantBSpifs[0].GetProperty("policyOid").GetString());
        Assert.NotEqual(tenantASpifs[0].GetProperty("id").GetGuid(), tenantBSpifs[0].GetProperty("id").GetGuid());
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
        using var request = CreateTenantRequest(HttpMethod.Get, "/pap/api/policy-sets", tenantId);
        using var response = await _fixture.HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.EnumerateArray()
            .Select(static item => item.GetProperty("id").GetString()!)
            .ToList();
    }

    private async Task ImportSpifAsync(string? tenantId, string importedBy)
    {
        var policyOid = importedBy.StartsWith("tenant-a-import", StringComparison.Ordinal)
            ? "1.2.3.4.10"
            : importedBy.StartsWith("tenant-b-import", StringComparison.Ordinal)
                ? "1.2.3.4.20"
                : $"1.2.3.4.{Math.Abs(importedBy.GetHashCode())}";

        var xml = TestSpifSamples.BasicPolicy.Replace("1.2.3.4", policyOid, StringComparison.Ordinal);

        using var request = CreateTenantRequest(HttpMethod.Post, "/pap/api/spifs/import", tenantId);
        request.Content = JsonContent.Create(new
        {
            xml,
            activate = true,
            setAsDefault = false,
            importedBy
        });

        using var response = await _fixture.HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task<List<JsonElement>> GetSpifsAsync(string? tenantId)
    {
        using var request = CreateTenantRequest(HttpMethod.Get, "/pap/api/spifs", tenantId);
        using var response = await _fixture.HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        return payload?.Select(static item => item.Clone()).ToList() ?? [];
    }

    private static HttpRequestMessage CreateTenantRequest(HttpMethod method, string uri, string? tenantId)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            request.Headers.Add("X-Tenant-Id", tenantId);
        }

        return request;
    }
}
