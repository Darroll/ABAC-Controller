using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Tests.Integration.Fixtures;

namespace AbacController.Tests.Integration.Docker;

/// <summary>
/// End-to-end tests for the bundled SPIF import admin endpoint
/// (<c>/pap/api/spifs/bundled</c>) against the shipped Docker image.
/// Verifies that:
///
/// <list type="number">
///   <item><description>The listing endpoint surfaces every
///   <c>*.spif.xml</c> file that ships in the container image under
///   <c>/app/data/seed-spifs</c> (the seven bundled sample policies).</description></item>
///   <item><description>POSTing the import drives all seven SPIFs into
///   the target tenant via the strict xmlspif.org schema parser — the
///   legacy-dialect normalizer transparently converts the bundled files
///   so no hand-editing is required.</description></item>
///   <item><description>The import is idempotent: a second POST on the
///   same tenant skips every file rather than re-importing.</description></item>
/// </list>
/// </summary>
[Collection("Docker")]
public sealed class SpifSeedServiceTests
{
    private static readonly string[] ExpectedBundledFiles =
    [
        "aus-pspf.spif.xml",
        "cui.spif.xml",
        "nato.spif.xml",
        "rfc3114-acme.spif.xml",
        "test-corp.spif.xml",
        "test-gov.spif.xml",
        "us-dod.spif.xml",
    ];

    private readonly AbacContainerFixture _fixture;

    public SpifSeedServiceTests(AbacContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Listing_ReturnsEveryBundledSpifFile()
    {
        var response = await _fixture.HttpClient.GetAsync("/pap/api/spifs/bundled");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listing = await response.Content.ReadFromJsonAsync<JsonElement>();
        var files = listing.GetProperty("files").EnumerateArray()
            .Select(e => e.GetString())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList();

        foreach (var expected in ExpectedBundledFiles)
        {
            Assert.Contains(expected, files);
        }
    }

    [Fact]
    public async Task ImportBundled_IntoFreshTenant_CommitsAllSevenSpifs()
    {
        var tenantId = $"bundled-{Guid.NewGuid():N}"[..16];

        // First import pass: everything should land.
        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/pap/api/spifs/bundled/import")
        {
            Content = JsonContent.Create(new { tenantId, skipExisting = true }),
        };
        firstRequest.Headers.Add("X-Tenant-Id", tenantId);

        using var firstResponse = await _fixture.HttpClient.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var firstResult = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var imported = firstResult.GetProperty("imported").EnumerateArray().ToList();
        var failed = firstResult.GetProperty("failed").EnumerateArray().ToList();

        Assert.Equal(tenantId, firstResult.GetProperty("tenantId").GetString());
        Assert.Empty(failed);
        Assert.Equal(ExpectedBundledFiles.Length, imported.Count);
        foreach (var row in imported)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("policyOid").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("name").GetString()));
        }

        // Second import pass: all should be skipped.
        using var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/pap/api/spifs/bundled/import")
        {
            Content = JsonContent.Create(new { tenantId, skipExisting = true }),
        };
        secondRequest.Headers.Add("X-Tenant-Id", tenantId);

        using var secondResponse = await _fixture.HttpClient.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var secondResult = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(secondResult.GetProperty("imported").EnumerateArray());
        Assert.Equal(
            ExpectedBundledFiles.Length,
            secondResult.GetProperty("skipped").EnumerateArray().Count());
        Assert.Empty(secondResult.GetProperty("failed").EnumerateArray());

        // Verify persistence via the tenant-scoped list endpoint.
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/pap/api/spifs");
        listRequest.Headers.Add("X-Tenant-Id", tenantId);
        using var listResponse = await _fixture.HttpClient.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        var spifs = await listResponse.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(spifs);
        Assert.Equal(ExpectedBundledFiles.Length, spifs!.Length);
        Assert.All(spifs, s => Assert.Equal(tenantId, s.GetProperty("tenantId").GetString()));
    }
}
