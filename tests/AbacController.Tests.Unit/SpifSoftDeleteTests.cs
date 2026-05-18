using AbacController.Api.Hosting;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Entities;
using AbacController.Pap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for the SPIF soft-delete behaviour: the global query filter hides
/// deleted rows from standard queries, the filtered unique index allows a
/// re-import of a soft-deleted OID, and <see cref="BundledSpifImporter"/>
/// revives previously deleted rows on re-import instead of inserting
/// duplicates.
/// </summary>
public sealed class SpifSoftDeleteTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AbacDbContext _db;
    private readonly InMemorySpifRegistry _registry = new();
    private readonly StubAuditWriter _audit = new();
    private readonly StubPublisher _publisher = new();
    private readonly BundledSpifImporter _service;

    public SpifSoftDeleteTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"abac-softdelete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _service = new BundledSpifImporter(
            new SpifParser(),
            _registry,
            _db,
            _audit,
            _publisher,
            NullLogger<BundledSpifImporter>.Instance);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private void WriteSpif(string fileName, string xml) =>
        File.WriteAllText(Path.Combine(_tempDir, fileName), xml);

    [Fact]
    public async Task QueryFilter_HidesSoftDeletedRows()
    {
        _db.Spifs.Add(new SpifEntity
        {
            Id = Guid.NewGuid(),
            PolicyOid = "1.2.3.4",
            Name = "Live",
            SchemaVersion = "3.0",
            Hash = "h1",
            TenantId = "default",
            RawXml = "<spif/>",
        });
        _db.Spifs.Add(new SpifEntity
        {
            Id = Guid.NewGuid(),
            PolicyOid = "9.9.9.9",
            Name = "Deleted",
            SchemaVersion = "3.0",
            Hash = "h2",
            TenantId = "default",
            RawXml = "<spif/>",
            IsDeleted = true,
            DeletedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var visible = await _db.Spifs.ToListAsync();
        Assert.Single(visible);
        Assert.Equal("1.2.3.4", visible[0].PolicyOid);

        var all = await _db.Spifs.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task BundledImport_ReImportOfSoftDeletedOid_RevivesExistingRow()
    {
        // Arrange: one live and one soft-deleted row, same tenant different OIDs.
        _db.Spifs.Add(new SpifEntity
        {
            Id = Guid.NewGuid(),
            PolicyOid = "1.2.3.4",
            Name = "TEST (deleted)",
            SchemaVersion = "3.0",
            Hash = "old-hash",
            TenantId = "default",
            RawXml = "<spif/>",
            IsDeleted = true,
            DeletedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        });
        await _db.SaveChangesAsync();

        // Act: bundled import of the same OID.
        WriteSpif("basic.spif.xml", TestSpifSamples.BasicPolicy);
        var result = await _service.ImportFromDirectoryAsync("default", _tempDir);

        // Assert: single row per (tenant, OID), now live with fresh hash.
        Assert.Single(result.Imported);
        Assert.Empty(result.Skipped);
        Assert.Empty(result.Failed);

        var all = await _db.Spifs.IgnoreQueryFilters().ToListAsync();
        Assert.Single(all);
        Assert.False(all[0].IsDeleted);
        Assert.Null(all[0].DeletedAt);
        Assert.NotEqual("old-hash", all[0].Hash);
    }

    [Fact]
    public async Task BundledImport_SkipExisting_IgnoresSoftDeletedRows()
    {
        // Arrange: soft-deleted row should NOT make skipExisting skip.
        _db.Spifs.Add(new SpifEntity
        {
            Id = Guid.NewGuid(),
            PolicyOid = "1.2.3.4",
            Name = "Deleted",
            SchemaVersion = "3.0",
            Hash = "h",
            TenantId = "default",
            RawXml = "<spif/>",
            IsDeleted = true,
            DeletedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        WriteSpif("basic.spif.xml", TestSpifSamples.BasicPolicy);
        var result = await _service.ImportFromDirectoryAsync(
            "default", _tempDir, skipExisting: true);

        Assert.Single(result.Imported);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public async Task BundledImport_SkipExisting_SkipsLiveRows()
    {
        // Sanity: live row with the same OID is correctly skipped.
        _db.Spifs.Add(new SpifEntity
        {
            Id = Guid.NewGuid(),
            PolicyOid = "1.2.3.4",
            Name = "Live",
            SchemaVersion = "3.0",
            Hash = "h",
            TenantId = "default",
            RawXml = "<spif/>",
        });
        await _db.SaveChangesAsync();

        WriteSpif("basic.spif.xml", TestSpifSamples.BasicPolicy);
        var result = await _service.ImportFromDirectoryAsync(
            "default", _tempDir, skipExisting: true);

        Assert.Empty(result.Imported);
        Assert.Single(result.Skipped);
    }

    // ── Stubs ──

    private sealed class InMemorySpifRegistry : ISpifRegistry
    {
        private readonly Dictionary<string, ISpifIndex> _byOid = new(StringComparer.Ordinal);

        public ISpifIndex? GetByPolicyOid(string policyOid) => _byOid.GetValueOrDefault(policyOid);
        public ISpifIndex? GetDefault() => _byOid.Values.FirstOrDefault();
        public void Register(ISpifIndex spifIndex) => _byOid[spifIndex.PolicyOid] = spifIndex;
        public void SetDefault(string policyOid) { }
        public void Remove(string policyOid) => _byOid.Remove(policyOid);
        public bool IsRegistered(string policyOid) => _byOid.ContainsKey(policyOid);
        public IReadOnlyList<string> GetRegisteredPolicyOids() => _byOid.Keys.ToList();
    }

    private sealed class StubAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = new();
        public void Write(AuditEvent auditEvent) => Events.Add(auditEvent);
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubPublisher : IWebhookPublisher
    {
        public List<(string EventType, object Payload, string? TenantId)> Events { get; } = new();
        public Task PublishAsync(string eventType, object payload, string? tenantId, CancellationToken ct = default)
        {
            Events.Add((eventType, payload, tenantId));
            return Task.CompletedTask;
        }
    }
}
