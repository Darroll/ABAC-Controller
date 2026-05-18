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
/// Unit tests for <see cref="BundledSpifImporter"/>: missing/empty source
/// directories short-circuit, valid files land under the caller-supplied
/// tenant with audit + webhook side-effects, already-imported policies
/// are skipped when <c>skipExisting=true</c>, and malformed files are
/// reported in the failed collection without blocking the rest of the
/// batch.
/// </summary>
public sealed class BundledSpifImporterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AbacDbContext _db;
    private readonly InMemorySpifRegistry _registry = new();
    private readonly StubAuditWriter _audit = new();
    private readonly StubPublisher _publisher = new();
    private readonly BundledSpifImporter _service;

    public BundledSpifImporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"abac-bundled-import-{Guid.NewGuid():N}");
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
    public async Task ImportFromDirectoryAsync_MissingDirectory_ReturnsEmpty()
    {
        var missing = Path.Combine(_tempDir, "missing");
        var result = await _service.ImportFromDirectoryAsync("default", missing);

        Assert.Empty(result.Imported);
        Assert.Empty(result.Skipped);
        Assert.Empty(result.Failed);
        Assert.False(result.HasChanges);
        Assert.Empty(await _db.Spifs.ToListAsync());
    }

    [Fact]
    public async Task ImportFromDirectoryAsync_EmptyDirectory_ReturnsEmpty()
    {
        var result = await _service.ImportFromDirectoryAsync("default", _tempDir);

        Assert.Empty(result.Imported);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public async Task ImportFromDirectoryAsync_ValidFile_PersistsWithTenantAndSideEffects()
    {
        WriteSpif("basic.spif.xml", TestSpifSamples.BasicPolicy);

        var result = await _service.ImportFromDirectoryAsync("tenant-bundled", _tempDir);

        Assert.Single(result.Imported);
        Assert.Equal("tenant-bundled", result.TenantId);
        Assert.True(result.HasChanges);

        var rows = await _db.Spifs.ToListAsync();
        Assert.Single(rows);
        Assert.Equal("tenant-bundled", rows[0].TenantId);
        Assert.Equal(BundledSpifImporter.ImportedByTag, rows[0].ImportedBy);
        Assert.NotEmpty(rows[0].Hash);

        Assert.Single(_audit.Events, e => e.ActionName == "bundled_import_spif");
        Assert.Single(_publisher.Events, e => e.EventType == WebhookEventTypes.SpifImported);
        Assert.True(_registry.Registered);
    }

    [Fact]
    public async Task ImportFromDirectoryAsync_AlreadyExisting_IsSkipped()
    {
        // Preload the destination tenant with the same policy OID.
        _db.Spifs.Add(new SpifEntity
        {
            Id = Guid.NewGuid(),
            PolicyOid = "1.2.3.4",
            Name = "PreExisting",
            SchemaVersion = "3.0",
            Hash = "hash",
            TenantId = "default",
            RawXml = "<spif/>",
        });
        await _db.SaveChangesAsync();

        WriteSpif("basic.spif.xml", TestSpifSamples.BasicPolicy);

        var result = await _service.ImportFromDirectoryAsync("default", _tempDir);

        Assert.Empty(result.Imported);
        Assert.Single(result.Skipped);
        Assert.Empty(result.Failed);
        Assert.Equal("1.2.3.4", result.Skipped[0].PolicyOid);

        // Row count unchanged.
        Assert.Single(await _db.Spifs.ToListAsync());
    }

    [Fact]
    public async Task ImportFromDirectoryAsync_MalformedFile_ReportedAsFailed_OthersContinue()
    {
        WriteSpif("broken.spif.xml", "<not-a-spif/>");
        WriteSpif("valid.spif.xml", TestSpifSamples.BasicPolicy);

        var result = await _service.ImportFromDirectoryAsync("default", _tempDir);

        Assert.Single(result.Imported);
        Assert.Single(result.Failed);
        Assert.Equal("broken.spif.xml", result.Failed[0].File);

        Assert.Single(await _db.Spifs.ToListAsync());
    }

    // ── Stubs ──

    private sealed class InMemorySpifRegistry : ISpifRegistry
    {
        private readonly Dictionary<string, ISpifIndex> _byOid = new(StringComparer.Ordinal);
        public bool Registered { get; private set; }

        public ISpifIndex? GetByPolicyOid(string policyOid) => _byOid.GetValueOrDefault(policyOid);
        public ISpifIndex? GetDefault() => _byOid.Values.FirstOrDefault();
        public void Register(ISpifIndex spifIndex)
        {
            _byOid[spifIndex.PolicyOid] = spifIndex;
            Registered = true;
        }
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
