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
/// Tests for <see cref="SpifSeedService"/>: missing directory short-circuits,
/// already-seeded tenant short-circuits, on-disk XML files are imported into
/// the registry + DB, and webhook + audit events fire after a successful seed.
/// </summary>
public sealed class SpifSeedServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AbacDbContext _db;
    private readonly InMemorySpifRegistry _registry = new();
    private readonly StubAuditWriter _audit = new();
    private readonly StubPublisher _publisher = new();
    private readonly SpifSeedService _service;

    public SpifSeedServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"abac-seed-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _service = new SpifSeedService(
            new SpifParser(),
            _registry,
            _db,
            _audit,
            _publisher,
            NullLogger<SpifSeedService>.Instance);
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
    public async Task Returns_Zero_WhenDirectoryMissing()
    {
        var seeded = await _service.SeedDefaultsAsync(Path.Combine(_tempDir, "missing"));
        Assert.Equal(0, seeded);
        Assert.Empty(await _db.Spifs.ToListAsync());
    }

    [Fact]
    public async Task Returns_Zero_WhenDirectoryEmpty()
    {
        var seeded = await _service.SeedDefaultsAsync(_tempDir);
        Assert.Equal(0, seeded);
    }

    [Fact]
    public async Task Returns_Zero_WhenDefaultTenantAlreadyHasSpifs()
    {
        _db.Spifs.Add(new SpifEntity
        {
            Id = Guid.NewGuid(),
            PolicyOid = "1.2.3.4",
            Name = "Existing",
            SchemaVersion = "3.0",
            Hash = "abc",
            TenantId = SpifSeedService.DefaultTenant,
            RawXml = "<spif/>"
        });
        await _db.SaveChangesAsync();

        WriteSpif("a.spif.xml", TestSpifSamples.BasicPolicy);

        var seeded = await _service.SeedDefaultsAsync(_tempDir);

        Assert.Equal(0, seeded);
        Assert.Single(await _db.Spifs.ToListAsync());
    }

    [Fact]
    public async Task Seeds_ValidSpifFile_PersistsAuditsAndPublishes()
    {
        WriteSpif("basic.spif.xml", TestSpifSamples.BasicPolicy);

        var seeded = await _service.SeedDefaultsAsync(_tempDir);

        Assert.Equal(1, seeded);
        var rows = await _db.Spifs.ToListAsync();
        Assert.Single(rows);
        Assert.Equal(SpifSeedService.DefaultTenant, rows[0].TenantId);
        Assert.True(rows[0].IsActive);
        Assert.Equal("system-seed", rows[0].ImportedBy);
        Assert.NotEmpty(rows[0].Hash);

        Assert.Single(_audit.Events, e => e.ActionName == "seed_spif");
        Assert.Single(_publisher.Events, e => e.EventType == WebhookEventTypes.SpifImported);
        Assert.True(_registry.Registered);
    }

    [Fact]
    public async Task SkipsInvalidFile_ButContinuesWithRest()
    {
        WriteSpif("broken.spif.xml", "<not-a-spif/>");
        WriteSpif("valid.spif.xml", TestSpifSamples.BasicPolicy);

        var seeded = await _service.SeedDefaultsAsync(_tempDir);

        Assert.Equal(1, seeded);
        var rows = await _db.Spifs.ToListAsync();
        Assert.Single(rows);
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
