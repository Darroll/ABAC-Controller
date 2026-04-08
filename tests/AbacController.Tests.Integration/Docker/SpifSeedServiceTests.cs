using System.Net.Http.Json;
using System.Text.Json;
using AbacController.Tests.Integration.Fixtures;

namespace AbacController.Tests.Integration.Docker;

/// <summary>
/// First-boot integration test for <c>SpifSeedService</c>. Starts a fresh
/// Docker container pointed at a caller-supplied seed directory, drops a
/// known-parseable SPIF XML into it, and verifies that the seeder picked
/// it up and committed it under the <c>default</c> tenant. Uses
/// <see cref="ConfigurableAbacContainer"/> so it does not share state with
/// the long-lived <see cref="AbacContainerFixture"/>.
///
/// The test deliberately uses an on-host directory bind-mounted at
/// <c>/data/seed-spifs</c> rather than the image-embedded
/// <c>/app/data/seed-spifs</c> folder because the 7 bundled seed files
/// (moved from Email Classification in Phase 2) target a looser XSD than
/// the ABAC parser enforces — a known follow-up tracked as a separate
/// Phase 2 item. This integration test validates the
/// <c>SpifSeedService</c> code path itself, not the bundled XML.
/// </summary>
public sealed class SpifSeedServiceTests
{
    [Fact]
    public async Task FirstBoot_WithSeedEnabled_ImportsSpifsFromSeedDirectory()
    {
        await using var container = new ConfigurableAbacContainer(new Dictionary<string, string>
        {
            ["ABAC_Auth__EnableDevelopmentAuth"] = "true",
            ["ABAC_Seed__SeedDefaultSpifs"] = "true",
            // Point the seeder at a sub-folder inside the bind-mounted /data volume
            // so the test can drop known-good SPIF XML into it from the host.
            ["ABAC_Seed__SpifSeedDirectory"] = "/data/seed-spifs",
        });

        // Drop a known-parseable SPIF fixture into the bind-mounted seed folder.
        var seedDir = Path.Combine(container.DataDirectory, "seed-spifs");
        Directory.CreateDirectory(seedDir);
        await File.WriteAllTextAsync(Path.Combine(seedDir, "integration-seed.spif.xml"),
            TestSpifSamples.BasicPolicy);

        await container.StartAsync();

        // The seeder attaches every row to tenantId="default", so read back
        // via the tenant-scoped list endpoint with X-Tenant-Id=default.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/pap/api/spifs");
        request.Headers.Add("X-Tenant-Id", "default");
        using var response = await container.HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var spifs = await response.Content.ReadFromJsonAsync<JsonElement[]>();

        if (spifs is null || spifs.Length == 0)
        {
            var logs = await container.GetLogsAsync();
            throw new Xunit.Sdk.XunitException(
                $"Seeded SPIFs list was empty. Container logs:\n{logs}");
        }

        Assert.Single(spifs!);
        var row = spifs![0];
        Assert.Equal("default", row.GetProperty("tenantId").GetString());
        Assert.Equal("1.2.3.4", row.GetProperty("policyOid").GetString());
        Assert.Equal("system-seed", row.GetProperty("importedBy").GetString());
    }
}
