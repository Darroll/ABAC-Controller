using System.Security.Cryptography;
using System.Text;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Entities;
using AbacController.Pdp;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Api.Hosting;

/// <summary>
/// Reads every <c>*.spif.xml</c> file under a given directory, parses each
/// via the standard <see cref="ISpifParser"/> pipeline, and writes the
/// resulting rows into the Spifs table for a caller-supplied tenant. This
/// is the shared implementation behind the bundled-SPIF import admin API
/// endpoint — not a startup DB seeder. Callers pick the tenant; callers
/// pick the directory (the default is the <c>/app/data/seed-spifs</c>
/// folder that ships in the container).
///
/// The writer mirrors <c>PapAdminController.ImportSpif</c>: rows get an
/// <c>importedBy</c> of <c>bundled-import</c>, an audit <c>policy_change</c>
/// event fires for each imported SPIF, and a
/// <see cref="WebhookEventTypes.SpifImported"/> event fans out after the
/// batch commits so subscribers (the Email Classification SignalR hub
/// among them) pick up the new policies immediately.
/// </summary>
public sealed class BundledSpifImporter
{
    /// <summary>Default target tenant when callers don't override.</summary>
    public const string DefaultTenant = "default";

    /// <summary>Imported-by tag stamped on every row this class writes.</summary>
    public const string ImportedByTag = "bundled-import";

    private readonly ISpifParser _spifParser;
    private readonly ISpifRegistry _spifRegistry;
    private readonly AbacDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;
    private readonly IWebhookPublisher _webhookPublisher;
    private readonly ILogger<BundledSpifImporter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BundledSpifImporter"/> class.
    /// </summary>
    public BundledSpifImporter(
        ISpifParser spifParser,
        ISpifRegistry spifRegistry,
        AbacDbContext dbContext,
        IAuditWriter auditWriter,
        IWebhookPublisher webhookPublisher,
        ILogger<BundledSpifImporter> logger)
    {
        _spifParser = spifParser;
        _spifRegistry = spifRegistry;
        _dbContext = dbContext;
        _auditWriter = auditWriter;
        _webhookPublisher = webhookPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Imports every <c>*.spif.xml</c> file under <paramref name="sourceDirectory"/>
    /// into <paramref name="tenantId"/>. When <paramref name="skipExisting"/> is
    /// true (the default) any file whose <c>policyOid</c> already exists for the
    /// target tenant is skipped and reported as <see cref="BundledSpifImportResult.Skipped"/>
    /// rather than overwriting the existing row.
    /// </summary>
    public async Task<BundledSpifImportResult> ImportFromDirectoryAsync(
        string tenantId,
        string sourceDirectory,
        bool skipExisting = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);

        if (!Directory.Exists(sourceDirectory))
        {
            _logger.LogWarning(
                "Bundled SPIF import source '{Directory}' does not exist.", sourceDirectory);
            return BundledSpifImportResult.Empty(sourceDirectory);
        }

        var files = Directory.GetFiles(sourceDirectory, "*.spif.xml");
        if (files.Length == 0)
        {
            _logger.LogInformation(
                "No *.spif.xml files found under '{Directory}'; nothing to import.", sourceDirectory);
            return BundledSpifImportResult.Empty(sourceDirectory);
        }

        var existingOids = await _dbContext.Spifs
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.PolicyOid)
            .ToListAsync(ct);
        var existing = new HashSet<string>(existingOids, StringComparer.Ordinal);

        var imported = new List<BundledSpifRecord>();
        var skipped = new List<BundledSpifRecord>();
        var failed = new List<BundledSpifFailure>();

        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);

            string xml;
            try
            {
                xml = await File.ReadAllTextAsync(file, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read bundled SPIF '{File}'.", file);
                failed.Add(new BundledSpifFailure(fileName, $"read error: {ex.Message}"));
                continue;
            }

            var parsed = _spifParser.Parse(xml);
            if (!parsed.Success || parsed.Spif is null)
            {
                var errorSummary = string.Join("; ", parsed.Errors.Select(e => e.Message));
                _logger.LogWarning(
                    "Failed to parse bundled SPIF '{File}': {Errors}", file, errorSummary);
                failed.Add(new BundledSpifFailure(fileName, errorSummary));
                continue;
            }

            if (skipExisting && existing.Contains(parsed.Spif.PolicyId.Oid))
            {
                _logger.LogInformation(
                    "Skipping already-imported SPIF '{Oid}' in tenant '{Tenant}'.",
                    parsed.Spif.PolicyId.Oid, tenantId);
                skipped.Add(new BundledSpifRecord(
                    fileName, parsed.Spif.PolicyId.Oid, parsed.Spif.PolicyId.Name));
                continue;
            }

            var spifIndex = new SpifIndex(parsed.Spif);
            _spifRegistry.Register(spifIndex);

            var entity = new SpifEntity
            {
                Id = Guid.NewGuid(),
                PolicyOid = spifIndex.PolicyOid,
                Name = spifIndex.PolicyName,
                SchemaVersion = parsed.Spif.SchemaVersion ?? "unknown",
                RawXml = xml,
                IsActive = true,
                ImportedAt = DateTimeOffset.UtcNow,
                ImportedBy = ImportedByTag,
                TenantId = tenantId,
                ClassificationCount = parsed.Spif.Classifications.Count,
                CategoryCount = parsed.Spif.CategoryTagSets.Sum(ts => ts.Tags.Sum(t => t.Categories.Count)),
                Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))),
            };

            _dbContext.Spifs.Add(entity);
            imported.Add(new BundledSpifRecord(fileName, entity.PolicyOid, entity.Name));
            existing.Add(entity.PolicyOid);

            _auditWriter.Write(new AuditEvent
            {
                EventType = "policy_change",
                ActionName = "bundled_import_spif",
                ResourceType = "spif",
                ResourceId = entity.PolicyOid,
                ActorIdentity = ImportedByTag,
                DetailJson = $"{{\"name\":\"{entity.Name}\",\"file\":\"{fileName}\"}}",
                TenantId = tenantId,
            });
        }

        if (imported.Count > 0)
        {
            await _dbContext.SaveChangesAsync(ct);

            foreach (var row in imported)
            {
                await _webhookPublisher.PublishAsync(
                    WebhookEventTypes.SpifImported,
                    new { row.PolicyOid, row.Name, source = "bundled" },
                    tenantId,
                    ct);
            }

            _logger.LogInformation(
                "Bundled SPIF import committed {Count} new policy(ies) into tenant '{Tenant}'.",
                imported.Count, tenantId);
        }

        return new BundledSpifImportResult(
            TenantId: tenantId,
            SourceDirectory: sourceDirectory,
            Imported: imported,
            Skipped: skipped,
            Failed: failed);
    }
}

/// <summary>One successfully imported or skipped SPIF.</summary>
public sealed record BundledSpifRecord(string File, string PolicyOid, string Name);

/// <summary>One SPIF whose import failed.</summary>
public sealed record BundledSpifFailure(string File, string Reason);

/// <summary>Aggregate outcome of a bundled SPIF import pass.</summary>
public sealed record BundledSpifImportResult(
    string TenantId,
    string SourceDirectory,
    IReadOnlyList<BundledSpifRecord> Imported,
    IReadOnlyList<BundledSpifRecord> Skipped,
    IReadOnlyList<BundledSpifFailure> Failed)
{
    /// <summary>Creates a no-op result for a missing or empty source directory.</summary>
    public static BundledSpifImportResult Empty(string sourceDirectory) => new(
        TenantId: string.Empty,
        SourceDirectory: sourceDirectory,
        Imported: Array.Empty<BundledSpifRecord>(),
        Skipped: Array.Empty<BundledSpifRecord>(),
        Failed: Array.Empty<BundledSpifFailure>());

    /// <summary>True when at least one SPIF was actually written.</summary>
    public bool HasChanges => Imported.Count > 0;
}
