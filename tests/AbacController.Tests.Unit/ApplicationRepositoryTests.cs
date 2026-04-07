using AbacController.Core.Domain.Classifications;
using AbacController.Data;
using AbacController.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

public sealed class ApplicationRepositoryTests : IDisposable
{
    private readonly AbacDbContext _db;
    private readonly ApplicationRepository _repo;

    public ApplicationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AbacDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _repo = new ApplicationRepository(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task UpsertAndGet_RoundTripsRegistration()
    {
        var registration = new ApplicationRegistration
        {
            Id = "email-app",
            Name = "Email Classification",
            Description = "Outlook add-in",
            DefaultPolicyOid = "1.2.3.4",
            AllowedClassificationLacvs = [1, 2, 3],
            MaxClassificationHierarchy = 3,
            AllowedTagSetOids = ["1.2.3.4.1"],
            IsActive = true
        };

        await _repo.UpsertAsync(registration);
        var result = await _repo.GetByIdAsync("email-app");

        Assert.NotNull(result);
        Assert.Equal("email-app", result!.Id);
        Assert.Equal("Email Classification", result.Name);
        Assert.Equal("Outlook add-in", result.Description);
        Assert.Equal("1.2.3.4", result.DefaultPolicyOid);
        Assert.Equal([1, 2, 3], result.AllowedClassificationLacvs);
        Assert.Equal(3, result.MaxClassificationHierarchy);
        Assert.Equal(["1.2.3.4.1"], result.AllowedTagSetOids);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task Upsert_UpdatesExistingRegistration()
    {
        var original = new ApplicationRegistration
        {
            Id = "email-app",
            Name = "Email v1",
            AllowedClassificationLacvs = [1, 2]
        };
        await _repo.UpsertAsync(original);

        var updated = new ApplicationRegistration
        {
            Id = "email-app",
            Name = "Email v2",
            AllowedClassificationLacvs = [1, 2, 3],
            MaxClassificationHierarchy = 3
        };
        await _repo.UpsertAsync(updated);

        var result = await _repo.GetByIdAsync("email-app");
        Assert.NotNull(result);
        Assert.Equal("Email v2", result!.Name);
        Assert.Equal([1, 2, 3], result.AllowedClassificationLacvs);
        Assert.Equal(3, result.MaxClassificationHierarchy);
    }

    [Fact]
    public async Task Delete_RemovesRegistration()
    {
        await _repo.UpsertAsync(new ApplicationRegistration
        {
            Id = "to-delete",
            Name = "Test"
        });

        var deleted = await _repo.DeleteAsync("to-delete");
        Assert.True(deleted);

        var result = await _repo.GetByIdAsync("to-delete");
        Assert.Null(result);
    }

    [Fact]
    public async Task Delete_ReturnsFalse_WhenNotFound()
    {
        var deleted = await _repo.DeleteAsync("nonexistent");
        Assert.False(deleted);
    }

    [Fact]
    public async Task GetAll_ReturnsAllRegistrations()
    {
        await _repo.UpsertAsync(new ApplicationRegistration { Id = "app-1", Name = "App 1" });
        await _repo.UpsertAsync(new ApplicationRegistration { Id = "app-2", Name = "App 2" });

        var all = await _repo.GetAllAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _repo.GetByIdAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task EmptyLists_RoundTrip_AsEmptyArrays()
    {
        await _repo.UpsertAsync(new ApplicationRegistration
        {
            Id = "empty-app",
            Name = "Empty",
            AllowedClassificationLacvs = [],
            AllowedTagSetOids = []
        });

        var result = await _repo.GetByIdAsync("empty-app");
        Assert.NotNull(result);
        Assert.Empty(result!.AllowedClassificationLacvs);
        Assert.Empty(result.AllowedTagSetOids);
    }
}
