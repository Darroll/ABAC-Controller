using AbacController.Core.Domain.Audit;
using AbacController.Data;
using AbacController.Data.Entities;
using AbacController.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for AuditRepository — GetById and data mapping.
/// Query/sorting tests are covered by integration tests (SQLite doesn't support
/// DateTimeOffset in ORDER BY without production configuration).
/// </summary>
public sealed class AuditRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AbacDbContext _db;
    private readonly AuditRepository _repo;

    public AuditRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new AuditRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetByIdAsync_ExistingEvent_ReturnsEvent()
    {
        var id = Guid.NewGuid();
        _db.AuditEvents.Add(new AuditEventEntity
        {
            Id = id,
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "evaluation",
            SubjectId = "alice",
            Decision = "Permit"
        });
        await _db.SaveChangesAsync();

        var result = await _repo.GetByIdAsync(id);
        Assert.NotNull(result);
        Assert.Equal(id, result.Id);
        Assert.Equal("evaluation", result.EventType);
        Assert.Equal("alice", result.SubjectId);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_MapsAllFields()
    {
        var id = Guid.NewGuid();
        _db.AuditEvents.Add(new AuditEventEntity
        {
            Id = id,
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "evaluation",
            RequestId = "req-1",
            DecisionId = "dec-1",
            SubjectType = "user",
            SubjectId = "alice",
            ActionName = "read",
            ResourceType = "document",
            ResourceId = "doc-1",
            Decision = "Permit",
            AppliedPolicies = "[\"policy-1\"]",
            EvaluationTimeMs = 5.5,
            PepId = "pep-1",
            ActorIdentity = "system",
            DetailJson = "{\"key\":\"value\"}"
        });
        await _db.SaveChangesAsync();

        var result = await _repo.GetByIdAsync(id);
        Assert.NotNull(result);
        Assert.Equal("evaluation", result.EventType);
        Assert.Equal("req-1", result.RequestId);
        Assert.Equal("dec-1", result.DecisionId);
        Assert.Equal("user", result.SubjectType);
        Assert.Equal("alice", result.SubjectId);
        Assert.Equal("read", result.ActionName);
        Assert.Equal("document", result.ResourceType);
        Assert.Equal("doc-1", result.ResourceId);
        Assert.Equal("Permit", result.Decision);
        Assert.Equal("[\"policy-1\"]", result.AppliedPolicies);
        Assert.Equal(5.5, result.EvaluationTimeMs);
        Assert.Equal("pep-1", result.PepId);
        Assert.Equal("system", result.ActorIdentity);
        Assert.Equal("{\"key\":\"value\"}", result.DetailJson);
    }

    [Fact]
    public async Task GetByIdAsync_MinimalEvent_MapsCorrectly()
    {
        var id = Guid.NewGuid();
        _db.AuditEvents.Add(new AuditEventEntity
        {
            Id = id,
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "system"
        });
        await _db.SaveChangesAsync();

        var result = await _repo.GetByIdAsync(id);
        Assert.NotNull(result);
        Assert.Equal("system", result.EventType);
        Assert.Null(result.SubjectId);
        Assert.Null(result.Decision);
    }
}
