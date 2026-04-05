using AbacController.Api.Runtime;
using AbacController.Core.Domain.Attributes;
using AbacController.Data;
using AbacController.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AbacController.Tests.Unit;

public sealed class DatabasePipSourceCatalogTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AbacDbContext _db;
    private readonly ServiceProvider _services;

    public DatabasePipSourceCatalogTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.EnsureCreated();

        _services = new ServiceCollection()
            .AddHttpClient()
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetSourcesAsync_BuildsStaticSource_FromPersistedSubjectsConfig()
    {
        _db.PipSources.Add(new PipSourceEntity
        {
            Id = "static-users",
            Name = "Static Users",
            SourceType = "static",
            ConfigJson = """
            {
              "subjects": {
                "alice": {
                  "department": "engineering",
                  "securityClearance": {
                    "policyOid": "1.2.3.4",
                    "classificationLacvs": [3]
                  }
                }
              }
            }
            """,
            ProvidesAttributes = "department,securityClearance",
            Priority = 10,
            CacheEnabled = true,
            CacheTtlSeconds = 300
        });
        await _db.SaveChangesAsync();

        var catalog = new DatabasePipSourceCatalog(_db, _services.GetRequiredService<IHttpClientFactory>());
        var source = Assert.Single(await catalog.GetSourcesAsync());

        var result = await source.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "alice",
            SubjectType = "user",
            RequestedAttributes = ["department", "securityClearance"]
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.Values.Count);
        Assert.Equal("engineering", result.Values.Single(v => v.Name == "department").Value);
        var clearance = Assert.IsType<AbacController.Core.Domain.Labels.SecurityClearance>(result.Values.Single(v => v.Name == "securityClearance").Value);
        Assert.Equal("1.2.3.4", clearance.PolicyOid);
        Assert.Contains(clearance.ClassificationLacvs, value => value.Value == 3);
    }

    [Fact]
    public async Task GetSourcesAsync_UsesWildcardAttributes_ForStaticConfig()
    {
        _db.PipSources.Add(new PipSourceEntity
        {
            Id = "static-defaults",
            Name = "Static Defaults",
            SourceType = "static",
            ConfigJson = """
            {
              "attributes": {
                "department": "engineering"
              }
            }
            """,
            ProvidesAttributes = "department",
            Priority = 10,
            CacheEnabled = true,
            CacheTtlSeconds = 300
        });
        await _db.SaveChangesAsync();

        var catalog = new DatabasePipSourceCatalog(_db, _services.GetRequiredService<IHttpClientFactory>());
        var source = Assert.Single(await catalog.GetSourcesAsync());

        var result = await source.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "whoever",
            SubjectType = "user",
            RequestedAttributes = ["department"]
        });

        Assert.True(result.Success);
        Assert.Equal("engineering", result.Values.Single().Value);
    }

    [Fact]
    public async Task GetSourcesAsync_ReportsInvalidConfig_AsUnhealthySource()
    {
        _db.PipSources.Add(new PipSourceEntity
        {
            Id = "bad-rest",
            Name = "Broken REST",
            SourceType = "rest",
            ConfigJson = "{}",
            ProvidesAttributes = "department",
            Priority = 10,
            CacheEnabled = true,
            CacheTtlSeconds = 300
        });
        await _db.SaveChangesAsync();

        var catalog = new DatabasePipSourceCatalog(_db, _services.GetRequiredService<IHttpClientFactory>());
        var source = Assert.Single(await catalog.GetSourcesAsync());
        var health = await source.TestConnectivityAsync();
        var resolution = await source.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = "alice",
            SubjectType = "user",
            RequestedAttributes = ["department"]
        });

        Assert.False(health.Healthy);
        Assert.Contains("urlTemplate", health.Message);
        Assert.False(resolution.Success);
        Assert.Contains("invalid", resolution.Error, StringComparison.OrdinalIgnoreCase);
    }
}
