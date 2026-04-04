using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Tests.Integration.Fixtures;

namespace AbacController.Tests.Integration.Docker;

[Collection("Docker")]
public sealed class SpifImportTests
{
    private readonly HttpClient _http;

    public SpifImportTests(AbacContainerFixture fixture)
    {
        _http = fixture.HttpClient;
    }

    [Fact]
    public async Task ImportSpif_ValidXml_ReturnsSuccess()
    {
        var body = new
        {
            xml = TestSpifSamples.BasicPolicy,
            activate = true,
            setAsDefault = true,
            importedBy = "integration-test"
        };

        var response = await _http.PostAsJsonAsync("/api/v1/pap/spifs/import", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(doc);

        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("1.2.3.4", root.GetProperty("policyOid").GetString());
        Assert.Equal("TEST", root.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ImportSpif_InvalidXml_ReturnsBadRequest()
    {
        var body = new
        {
            xml = "<notASpif>invalid</notASpif>",
            activate = true
        };

        var response = await _http.PostAsJsonAsync("/api/v1/pap/spifs/import", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListSpifs_AfterImport_ContainsImportedSpif()
    {
        // Import first
        var importBody = new
        {
            xml = TestSpifSamples.BasicPolicy,
            activate = true,
            setAsDefault = false,
            importedBy = "integration-test-list"
        };
        var importResponse = await _http.PostAsJsonAsync("/api/v1/pap/spifs/import", importBody);
        importResponse.EnsureSuccessStatusCode();

        // List
        var listResponse = await _http.GetAsync("/api/v1/pap/spifs");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var spifs = await listResponse.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(spifs);
        Assert.Contains(spifs, s =>
            s.TryGetProperty("policyOid", out var oid) && oid.GetString() == "1.2.3.4");
    }
}
