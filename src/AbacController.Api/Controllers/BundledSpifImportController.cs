using AbacController.Api.Configuration;
using AbacController.Api.Hosting;
using AbacController.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AbacController.Api.Controllers;

/// <summary>
/// Admin-scoped endpoints that import the SPIF XML files bundled in the
/// container image (under <c>/app/data/seed-spifs</c>) into a target
/// tenant on demand. This replaces the previous "first-boot auto-seed"
/// behaviour with an explicit, caller-driven import so operators pick
/// both the moment of import and the tenant the policies land in.
///
/// A <c>GET</c> on the bundled list returns what the import would see
/// without writing anything, and a <c>POST</c> drives the actual import
/// via <see cref="BundledSpifImporter.ImportFromDirectoryAsync"/>.
/// </summary>
[ApiController]
[Route("pap/api/spifs/bundled")]
[Authorize(Policy = "PolicyAdmin")]
public sealed class BundledSpifImportController : ControllerBase
{
    private readonly BundledSpifImporter _importer;
    private readonly ITenantContext _tenantContext;
    private readonly SeedOptions _seedOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="BundledSpifImportController"/> class.
    /// </summary>
    public BundledSpifImportController(
        BundledSpifImporter importer,
        ITenantContext tenantContext,
        IOptions<AbacControllerOptions> options)
    {
        _importer = importer;
        _tenantContext = tenantContext;
        _seedOptions = options.Value.Seed ?? new SeedOptions();
    }

    /// <summary>
    /// Lists the <c>*.spif.xml</c> files visible in the bundled import
    /// directory without importing anything. Useful as a discovery step
    /// from admin UIs.
    /// </summary>
    [HttpGet]
    public ActionResult<BundledSpifListing> List()
    {
        var directory = ResolveSourceDirectory();
        if (!Directory.Exists(directory))
        {
            return Ok(new BundledSpifListing(directory, Array.Empty<string>()));
        }

        var files = Directory.GetFiles(directory, "*.spif.xml")
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new BundledSpifListing(directory, files));
    }

    /// <summary>
    /// Imports every bundled SPIF into the current tenant (or the
    /// request-supplied override). Idempotent by default: any SPIF whose
    /// <c>policyOid</c> already exists for the target tenant is reported
    /// as skipped rather than overwritten.
    /// </summary>
    [HttpPost("import")]
    public async Task<ActionResult<BundledSpifImportResult>> Import(
        [FromBody] BundledSpifImportRequest? request,
        CancellationToken ct)
    {
        var tenantId = !string.IsNullOrWhiteSpace(request?.TenantId)
            ? request!.TenantId!
            : (!string.IsNullOrWhiteSpace(_tenantContext.TenantId)
                ? _tenantContext.TenantId!
                : BundledSpifImporter.DefaultTenant);

        var sourceDirectory = !string.IsNullOrWhiteSpace(request?.SourceDirectory)
            ? request!.SourceDirectory!
            : ResolveSourceDirectory();

        var skipExisting = request?.SkipExisting ?? true;

        var result = await _importer
            .ImportFromDirectoryAsync(tenantId, sourceDirectory, skipExisting, ct)
            .ConfigureAwait(false);

        return Ok(result);
    }

    private string ResolveSourceDirectory()
        => string.IsNullOrWhiteSpace(_seedOptions.SpifSeedDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "data", "seed-spifs")
            : _seedOptions.SpifSeedDirectory!;

    /// <summary>Request body for <c>POST /pap/api/spifs/bundled/import</c>.</summary>
    public sealed class BundledSpifImportRequest
    {
        /// <summary>Target tenant id. Defaults to the request's X-Tenant-Id, then "default".</summary>
        public string? TenantId { get; set; }

        /// <summary>Override the source directory. Defaults to the bundled seed directory.</summary>
        public string? SourceDirectory { get; set; }

        /// <summary>When true (default), skip SPIFs whose policyOid already exists for the tenant.</summary>
        public bool? SkipExisting { get; set; }
    }

    /// <summary>Response body for <c>GET /pap/api/spifs/bundled</c>.</summary>
    public sealed record BundledSpifListing(string SourceDirectory, IReadOnlyList<string> Files);
}
