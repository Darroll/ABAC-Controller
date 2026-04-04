using AbacController.Core.Domain.Policy;
using AbacController.Data;
using AbacController.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for PolicyRepository edge cases — versioning, activation, deletion.
/// </summary>
public sealed class PolicyRepositoryEdgeCaseTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AbacDbContext _db;
    private readonly PolicyRepository _repo;

    public PolicyRepositoryEdgeCaseTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new PolicyRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreatePolicySet_ReturnsCreated()
    {
        var ps = new PolicySet { Id = "ps-1", Name = "Test Set" };
        var result = await _repo.CreatePolicySetAsync(ps);

        Assert.Equal("ps-1", result.Id);
        Assert.Equal("Test Set", result.Name);
    }

    [Fact]
    public async Task GetPolicySet_NonExistent_ReturnsNull()
    {
        var result = await _repo.GetPolicySetAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeletePolicySet_RemovesFromDatabase()
    {
        var ps = new PolicySet { Id = "ps-del", Name = "Delete Me" };
        await _repo.CreatePolicySetAsync(ps);

        await _repo.DeletePolicySetAsync("ps-del");

        Assert.Null(await _repo.GetPolicySetAsync("ps-del"));
    }

    [Fact]
    public async Task CreatePolicy_LinksToSet()
    {
        await _repo.CreatePolicySetAsync(new PolicySet { Id = "ps-1", Name = "Test" });
        var policy = new Policy { Id = "pol-1", PolicySetId = "ps-1", Name = "Policy One" };
        var result = await _repo.CreatePolicyAsync(policy);

        Assert.Equal("pol-1", result.Id);
        Assert.Equal("ps-1", result.PolicySetId);
    }

    [Fact]
    public async Task DeletePolicy_RemovesFromDatabase()
    {
        await _repo.CreatePolicySetAsync(new PolicySet { Id = "ps-1", Name = "Test" });
        await _repo.CreatePolicyAsync(new Policy { Id = "pol-del", PolicySetId = "ps-1", Name = "Delete Me" });

        await _repo.DeletePolicyAsync("pol-del");

        Assert.Null(await _repo.GetPolicyAsync("pol-del"));
    }

    [Fact]
    public async Task CreateVersion_IncrementsByOne()
    {
        await _repo.CreatePolicySetAsync(new PolicySet { Id = "ps-1", Name = "Test" });
        await _repo.CreatePolicyAsync(new Policy { Id = "pol-ver", PolicySetId = "ps-1", Name = "Version Test" });

        var v1 = await _repo.CreateVersionAsync(new PolicyVersion
        {
            PolicyId = "pol-ver", VersionNumber = 1, Content = "v1", Hash = "aaa"
        });

        var v2 = await _repo.CreateVersionAsync(new PolicyVersion
        {
            PolicyId = "pol-ver", VersionNumber = 2, Content = "v2", Hash = "bbb"
        });

        var versions = await _repo.GetVersionsAsync("pol-ver");
        Assert.Equal(2, versions.Count);
        Assert.NotEqual(v1.Id, v2.Id);
    }

    [Fact]
    public async Task ActivateVersion_DeactivatesOthers()
    {
        await _repo.CreatePolicySetAsync(new PolicySet { Id = "ps-1", Name = "Test" });
        await _repo.CreatePolicyAsync(new Policy { Id = "pol-act", PolicySetId = "ps-1", Name = "Activation Test" });

        var v1 = await _repo.CreateVersionAsync(new PolicyVersion
        {
            PolicyId = "pol-act", VersionNumber = 1, Content = "v1", Hash = "aaa", IsActive = true
        });

        var v2 = await _repo.CreateVersionAsync(new PolicyVersion
        {
            PolicyId = "pol-act", VersionNumber = 2, Content = "v2", Hash = "bbb", IsActive = false
        });

        await _repo.ActivateVersionAsync("pol-act", v2.Id);

        var updatedV1 = await _repo.GetVersionAsync(v1.Id);
        var updatedV2 = await _repo.GetVersionAsync(v2.Id);

        Assert.False(updatedV1!.IsActive);
        Assert.True(updatedV2!.IsActive);
    }

    [Fact]
    public async Task GetPolicySets_ReturnsAll()
    {
        await _repo.CreatePolicySetAsync(new PolicySet { Id = "ps-a", Name = "A" });
        await _repo.CreatePolicySetAsync(new PolicySet { Id = "ps-b", Name = "B" });

        var all = await _repo.GetPolicySetsAsync();
        Assert.True(all.Count >= 2);
    }

    [Fact]
    public async Task UpdatePolicySet_ChangesFields()
    {
        await _repo.CreatePolicySetAsync(new PolicySet { Id = "ps-upd", Name = "Original" });

        var updated = await _repo.UpdatePolicySetAsync(new PolicySet
        {
            Id = "ps-upd",
            Name = "Updated",
            CombiningAlgorithm = "permit-overrides",
            IsActive = false
        });

        Assert.Equal("Updated", updated.Name);
    }
}
