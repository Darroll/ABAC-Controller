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
/// Imports the bundled default SPIF documents into ABAC's Spifs table on
/// first boot. Idempotent: only seeds the default tenant when no SPIFs
/// already exist for it. Mirrors <c>PapAdminController.ImportSpif</c>'s
/// write path so the seed and the admin import behave identically.
///
/// Lives in the API project (not Pap) because it composes services from
/// across the dependency graph (Pap parser + Pdp SpifIndex + Data DbContext
/// + webhook publisher).
/// </summary>
public sealed class SpifSeedService
{
    /// <summary>The tenant id seeded SPIFs are attached to.</summary>
    public const string DefaultTenant = "default";

    private readonly ISpifParser _spifParser;
    private readonly ISpifRegistry _spifRegistry;
    private readonly AbacDbContext _dbContext;
    private readonly IAuditWriter _auditWriter;
    private readonly IWebhookPublisher _webhookPublisher;
    private readonly ILogger<SpifSeedService> _logger;

    public SpifSeedService(
        ISpifParser spifParser,
        ISpifRegistry spifRegistry,
        AbacDbContext dbContext,
        IAuditWriter auditWriter,
        IWebhookPublisher webhookPublisher,
        ILogger<SpifSeedService> logger)
    {
        _spifParser = spifParser;
        _spifRegistry = spifRegistry;
        _dbContext = dbContext;
        _auditWriter = auditWriter;
        _webhookPublisher = webhookPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Seed every <c>*.spif.xml</c> file under <paramref name="seedDirectory"/>.
    /// No-op when the default tenant already has any SPIF rows or the directory
    /// is missing/empty.
    /// </summary>
    /// <returns>The number of SPIFs seeded on this call.</returns>
    public async Task<int> SeedDefaultsAsync(string seedDirectory, CancellationToken ct = default)
    {
        if (!Directory.Exists(seedDirectory))
        {
            _logger.LogDebug("SPIF seed directory '{Directory}' does not exist; skipping seed.", seedDirectory);
            return 0;
        }

        // Tenant-aware idempotency: skip if the default tenant already has any
        // SPIFs. Other tenants are managed via the admin API and we don't touch
        // them.
        var alreadySeeded = await _dbContext.Spifs
            .AnyAsync(s => s.TenantId == DefaultTenant || s.TenantId == null, ct);
        if (alreadySeeded)
        {
            _logger.LogDebug("Default tenant already has SPIFs; skipping seed.");
            return 0;
        }

        var files = Directory.GetFiles(seedDirectory, "*.spif.xml");
        if (files.Length == 0)
        {
            _logger.LogDebug("No *.spif.xml files found under '{Directory}'; nothing to seed.", seedDirectory);
            return 0;
        }

        var seededOids = new List<(string PolicyOid, string Name, string SchemaVersion)>();

        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var xml = await File.ReadAllTextAsync(file, ct);
                var parsed = _spifParser.Parse(xml);
                if (!parsed.Success || parsed.Spif is null)
                {
                    _logger.LogWarning("Failed to parse seed SPIF '{File}': {Errors}",
                        file, string.Join("; ", parsed.Errors.Select(e => e.Message)));
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
                    ImportedBy = "system-seed",
                    TenantId = DefaultTenant,
                    ClassificationCount = parsed.Spif.Classifications.Count,
                    CategoryCount = parsed.Spif.CategoryTagSets.Sum(ts => ts.Tags.Sum(t => t.Categories.Count)),
                    Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)))
                };

                _dbContext.Spifs.Add(entity);
                seededOids.Add((spifIndex.PolicyOid, spifIndex.PolicyName, entity.SchemaVersion));

                _auditWriter.Write(new AuditEvent
                {
                    EventType = "policy_change",
                    ActionName = "seed_spif",
                    ResourceType = "spif",
                    ResourceId = spifIndex.PolicyOid,
                    ActorIdentity = "system-seed",
                    DetailJson = $"{{\"name\":\"{spifIndex.PolicyName}\",\"file\":\"{Path.GetFileName(file)}\"}}",
                    TenantId = DefaultTenant
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping seed SPIF '{File}' due to error.", file);
            }
        }

        if (seededOids.Count == 0)
        {
            return 0;
        }

        await _dbContext.SaveChangesAsync(ct);

        // Emit webhook events AFTER the rows are durable so subscribers can
        // immediately fetch the new SPIFs without races.
        foreach (var (policyOid, name, schemaVersion) in seededOids)
        {
            await _webhookPublisher.PublishAsync(
                WebhookEventTypes.SpifImported,
                new { policyOid, name, schemaVersion, seeded = true },
                DefaultTenant,
                ct);
        }

        _logger.LogInformation("Seeded {Count} default SPIFs into the '{Tenant}' tenant.", seededOids.Count, DefaultTenant);
        return seededOids.Count;
    }
}
