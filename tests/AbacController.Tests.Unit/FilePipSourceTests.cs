using AbacController.Core.Domain.Attributes;
using AbacController.Pip.Sources;

namespace AbacController.Tests.Unit;

public sealed class FilePipSourceTests : IDisposable
{
    private readonly string _tempDir;

    public FilePipSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "abac-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task JsonFile_ResolvesAttributes()
    {
        var jsonPath = Path.Combine(_tempDir, "users.json");
        File.WriteAllText(jsonPath, """
        {
            "alice": { "department": "ENG", "clearanceLevel": 3, "active": true },
            "bob": { "department": "SALES", "clearanceLevel": 1, "active": false }
        }
        """);

        var source = new FilePipSource("file-test", 10, jsonPath);

        var result = await source.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "alice",
            SubjectType = "user",
            RequestedAttributes = ["department", "clearanceLevel", "active"]
        });

        Assert.True(result.Success);
        Assert.Equal(3, result.Values.Count);
        Assert.Equal("ENG", result.Values.First(v => v.Name == "department").Value);
        Assert.Equal(3L, result.Values.First(v => v.Name == "clearanceLevel").Value);
        Assert.Equal(true, result.Values.First(v => v.Name == "active").Value);
    }

    [Fact]
    public async Task CsvFile_ResolvesAttributes()
    {
        var csvPath = Path.Combine(_tempDir, "users.csv");
        File.WriteAllText(csvPath, """
        subjectId,department,clearanceLevel,active
        alice,ENG,3,true
        bob,SALES,1,false
        """);

        var source = new FilePipSource("file-csv-test", 10, csvPath);

        var result = await source.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "alice",
            SubjectType = "user",
            RequestedAttributes = ["department", "clearanceLevel"]
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.Values.Count);
        Assert.Equal("ENG", result.Values.First(v => v.Name == "department").Value);
        Assert.Equal(3L, result.Values.First(v => v.Name == "clearanceLevel").Value);
    }

    [Fact]
    public async Task ResolveAsync_UnknownSubject_ReturnsEmptyValues()
    {
        var jsonPath = Path.Combine(_tempDir, "users2.json");
        File.WriteAllText(jsonPath, """{ "alice": { "department": "ENG" } }""");

        var source = new FilePipSource("file-test", 10, jsonPath);

        var result = await source.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "unknown-user",
            SubjectType = "user",
            RequestedAttributes = ["department"]
        });

        Assert.True(result.Success);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task TestConnectivity_ReturnsHealthy_WhenFileExists()
    {
        var jsonPath = Path.Combine(_tempDir, "users3.json");
        File.WriteAllText(jsonPath, "{}");

        var source = new FilePipSource("file-test", 10, jsonPath);
        var health = await source.TestConnectivityAsync();

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task TestConnectivity_ReturnsUnhealthy_WhenFileMissing()
    {
        var source = new FilePipSource("file-test", 10, "/nonexistent/file.json");
        var health = await source.TestConnectivityAsync();

        Assert.False(health.Healthy);
    }

    [Fact]
    public void ProvidesAttributes_ReflectsFileContent()
    {
        var jsonPath = Path.Combine(_tempDir, "attrs.json");
        File.WriteAllText(jsonPath, """
        {
            "alice": { "department": "ENG", "role": "developer" },
            "bob": { "department": "SALES" }
        }
        """);

        var source = new FilePipSource("file-test", 10, jsonPath);
        var attrs = source.ProvidesAttributes;

        Assert.Contains("department", attrs);
        Assert.Contains("role", attrs);
    }

    [Fact]
    public async Task HotReload_PicksUpFileChanges()
    {
        var jsonPath = Path.Combine(_tempDir, "hot.json");
        File.WriteAllText(jsonPath, """{ "alice": { "department": "ENG" } }""");

        var source = new FilePipSource("file-test", 10, jsonPath);

        var result1 = await source.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "alice",
            SubjectType = "user",
            RequestedAttributes = ["department"]
        });
        Assert.Equal("ENG", result1.Values[0].Value);

        // Wait to ensure file timestamp changes
        await Task.Delay(50);
        File.WriteAllText(jsonPath, """{ "alice": { "department": "SALES" } }""");

        var result2 = await source.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "alice",
            SubjectType = "user",
            RequestedAttributes = ["department"]
        });
        Assert.Equal("SALES", result2.Values[0].Value);
    }
}
