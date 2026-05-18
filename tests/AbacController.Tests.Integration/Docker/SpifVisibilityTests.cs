using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Tests.Integration.Fixtures;

namespace AbacController.Tests.Integration.Docker;

/// <summary>
/// End-to-end tests for the Phase 2 <c>/pdp/api/spifs/visible</c> endpoint
/// against the shipped Docker image. Verifies the visibility formula
/// <c>visible = (PolicyOids ∪ ClassificationLacvsByPolicy.Keys) \ DeniedPolicyOids</c>
/// by importing a SPIF, granting a baseline entitlement, and confirming the
/// SPIF shows up in the subject's visible set.
/// </summary>
[Collection("Docker")]
public sealed class SpifVisibilityTests
{
    private readonly AbacContainerFixture _fixture;

    public SpifVisibilityTests(AbacContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Visible_AfterBaselineGrant_IncludesImportedSpif()
    {
        var tenantId = $"tenant-visible-{Guid.NewGuid():N}"[..24];

        // Import a tenant-scoped SPIF.
        var importBody = new
        {
            xml = TestSpifSamples.BasicPolicy,
            activate = true,
            setAsDefault = true,
            importedBy = "visibility-test"
        };
        var importResponse = await SendAsync(HttpMethod.Post, "/pap/api/spifs/import", tenantId, importBody);
        importResponse.EnsureSuccessStatusCode();
        var imported = await importResponse.Content.ReadFromJsonAsync<JsonElement>();
        var policyOid = imported.GetProperty("policyOid").GetString()!;

        // Grant a baseline entitlement for the whole policy (no classification).
        var baselineBody = new { policyOid, classificationLacv = (int?)null, tagSetOid = (string?)null };
        var baselineResponse = await SendAsync(
            HttpMethod.Post, $"/pap/api/entitlements/{tenantId}/baseline", tenantId, baselineBody);
        baselineResponse.EnsureSuccessStatusCode();

        // Call the visibility endpoint.
        var visibleResponse = await SendAsync(HttpMethod.Get, "/pdp/api/spifs/visible", tenantId);
        Assert.Equal(HttpStatusCode.OK, visibleResponse.StatusCode);
        var visible = await visibleResponse.Content.ReadFromJsonAsync<JsonElement>();

        var spifs = visible.GetProperty("spifs").EnumerateArray().ToList();
        Assert.Contains(spifs, s =>
            s.GetProperty("policyOid").GetString() == policyOid
            && s.TryGetProperty("name", out var _)
            && s.TryGetProperty("schemaVersion", out var _)
            && s.TryGetProperty("hash", out var _));
        Assert.Equal(policyOid, visible.GetProperty("defaultPolicyOid").GetString());
    }

    [Fact]
    public async Task Visible_WithoutAnyGrants_ReturnsEmptySpifs()
    {
        var tenantId = $"tenant-visible-none-{Guid.NewGuid():N}"[..24];

        var response = await SendAsync(HttpMethod.Get, "/pdp/api/spifs/visible", tenantId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var visible = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, visible.GetProperty("spifs").GetArrayLength());
        Assert.True(
            visible.GetProperty("defaultPolicyOid").ValueKind == JsonValueKind.Null
            || string.IsNullOrEmpty(visible.GetProperty("defaultPolicyOid").GetString()));
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? tenantId,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            request.Headers.Add("X-Tenant-Id", tenantId);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _fixture.HttpClient.SendAsync(request);
    }
}
