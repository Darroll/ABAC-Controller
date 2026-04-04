using System.Security.Cryptography;
using System.Text;
using AbacController.Core.Domain.Policy;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Entities;
using AbacController.Pdp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Api.Controllers;

/// <summary>
/// PAP (Policy Administration Point) endpoints for managing policy sets, policies, versions, and SPIFs.
/// </summary>
[ApiController]
[Route("api/v1/pap")]
public sealed class PapAdminController : ControllerBase
{
    private readonly IPolicyRepository _policyRepository;
    private readonly ISpifParser _spifParser;
    private readonly ISpifRegistry _spifRegistry;
    private readonly AbacDbContext _dbContext;

    public PapAdminController(
        IPolicyRepository policyRepository,
        ISpifParser spifParser,
        ISpifRegistry spifRegistry,
        AbacDbContext dbContext)
    {
        _policyRepository = policyRepository;
        _spifParser = spifParser;
        _spifRegistry = spifRegistry;
        _dbContext = dbContext;
    }

    [HttpGet("policy-sets")]
    [Authorize(Policy = "PolicyRead")]
    public Task<List<Core.Domain.Policy.PolicySet>> ListPolicySets(CancellationToken ct)
        => _policyRepository.GetPolicySetsAsync(ct);

    [HttpGet("policy-sets/{id}")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<ActionResult<Core.Domain.Policy.PolicySet>> GetPolicySet(string id, CancellationToken ct)
    {
        var item = await _policyRepository.GetPolicySetAsync(id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPut("policy-sets/{id}")]
    [Authorize(Policy = "PolicyWrite")]
    public async Task<ActionResult<Core.Domain.Policy.PolicySet>> UpsertPolicySet(string id, [FromBody] Core.Domain.Policy.PolicySet policySet, CancellationToken ct)
    {
        if (!string.Equals(id, policySet.Id, StringComparison.Ordinal))
            return BadRequest("Route id must match policySet.id");

        var existing = await _policyRepository.GetPolicySetAsync(id, ct);
        var saved = existing is null
            ? await _policyRepository.CreatePolicySetAsync(policySet, ct)
            : await _policyRepository.UpdatePolicySetAsync(policySet, ct);

        return Ok(saved);
    }

    [HttpDelete("policy-sets/{id}")]
    [Authorize(Policy = "PolicyAdmin")]
    public async Task<IActionResult> DeletePolicySet(string id, CancellationToken ct)
    {
        await _policyRepository.DeletePolicySetAsync(id, ct);
        return NoContent();
    }

    [HttpGet("policies/{id}")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<ActionResult<Core.Domain.Policy.Policy>> GetPolicy(string id, CancellationToken ct)
    {
        var item = await _policyRepository.GetPolicyAsync(id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPut("policies/{id}")]
    [Authorize(Policy = "PolicyWrite")]
    public async Task<ActionResult<Core.Domain.Policy.Policy>> UpsertPolicy(string id, [FromBody] Core.Domain.Policy.Policy policy, CancellationToken ct)
    {
        if (!string.Equals(id, policy.Id, StringComparison.Ordinal))
            return BadRequest("Route id must match policy.id");

        var existing = await _policyRepository.GetPolicyAsync(id, ct);
        var saved = existing is null
            ? await _policyRepository.CreatePolicyAsync(policy, ct)
            : await _policyRepository.UpdatePolicyAsync(policy, ct);

        return Ok(saved);
    }

    [HttpDelete("policies/{id}")]
    [Authorize(Policy = "PolicyAdmin")]
    public async Task<IActionResult> DeletePolicy(string id, CancellationToken ct)
    {
        await _policyRepository.DeletePolicyAsync(id, ct);
        return NoContent();
    }

    [HttpGet("policies/{id}/versions")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<ActionResult<List<PolicyVersion>>> ListPolicyVersions(string id, CancellationToken ct)
        => Ok(await _policyRepository.GetVersionsAsync(id, ct));

    public sealed record CreatePolicyVersionRequest(string Content, string? CreatedBy = null, bool Activate = true);

    [HttpPost("policies/{id}/versions")]
    [Authorize(Policy = "PolicyWrite")]
    public async Task<ActionResult<PolicyVersion>> CreatePolicyVersion(string id, [FromBody] CreatePolicyVersionRequest request, CancellationToken ct)
    {
        var policy = await _policyRepository.GetPolicyAsync(id, ct);
        if (policy is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("content is required");

        var existingVersions = await _policyRepository.GetVersionsAsync(id, ct);
        var nextVersionNumber = existingVersions.Count == 0 ? 1 : existingVersions.Max(v => v.VersionNumber) + 1;

        var version = new PolicyVersion
        {
            PolicyId = id,
            VersionNumber = nextVersionNumber,
            Content = request.Content,
            Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Content))),
            CreatedBy = request.CreatedBy,
            IsActive = request.Activate
        };

        var saved = await _policyRepository.CreateVersionAsync(version, ct);
        if (request.Activate)
        {
            await _policyRepository.ActivateVersionAsync(id, saved.Id, ct);
        }

        return Ok(await _policyRepository.GetVersionAsync(saved.Id, ct));
    }

    [HttpPost("policies/{id}/versions/{versionId:guid}/activate")]
    [Authorize(Policy = "PolicyWrite")]
    public async Task<IActionResult> ActivatePolicyVersion(string id, Guid versionId, CancellationToken ct)
    {
        var policy = await _policyRepository.GetPolicyAsync(id, ct);
        if (policy is null)
            return NotFound();

        var version = await _policyRepository.GetVersionAsync(versionId, ct);
        if (version is null || !string.Equals(version.PolicyId, id, StringComparison.Ordinal))
            return NotFound();

        await _policyRepository.ActivateVersionAsync(id, versionId, ct);
        return NoContent();
    }

    [HttpGet("spifs")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<ActionResult<List<SpifEntity>>> ListSpifs(CancellationToken ct)
    {
        var spifs = await _dbContext.Spifs.AsNoTracking().OrderBy(s => s.PolicyOid).ToListAsync(ct);
        return Ok(spifs);
    }

    public sealed record SpifImportRequest(string Xml, bool Activate = true, bool SetAsDefault = false, string? ImportedBy = null);

    [HttpPost("spifs/import")]
    [Authorize(Policy = "PolicyWrite")]
    public async Task<ActionResult<object>> ImportSpif([FromBody] SpifImportRequest request, CancellationToken ct)
    {
        var parsed = _spifParser.Parse(request.Xml);
        if (!parsed.Success || parsed.Spif is null)
            return BadRequest(new { success = false, errors = parsed.Errors });

        var spifIndex = new SpifIndex(parsed.Spif);
        _spifRegistry.Register(spifIndex);
        if (request.SetAsDefault)
            _spifRegistry.SetDefault(spifIndex.PolicyOid);

        var exists = await _dbContext.Spifs.FirstOrDefaultAsync(x => x.PolicyOid == spifIndex.PolicyOid, ct);
        var entity = exists ?? new SpifEntity { Id = Guid.NewGuid(), PolicyOid = spifIndex.PolicyOid };
        entity.Name = spifIndex.PolicyName;
        entity.SchemaVersion = parsed.Spif.SchemaVersion ?? "unknown";
        entity.RawXml = request.Xml;
        entity.IsActive = request.Activate;
        entity.ImportedAt = DateTimeOffset.UtcNow;
        entity.ImportedBy = request.ImportedBy;
        entity.ClassificationCount = parsed.Spif.Classifications.Count;
        entity.CategoryCount = parsed.Spif.CategoryTagSets.Sum(ts => ts.Tags.Sum(t => t.Categories.Count));
        entity.Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Xml)));

        if (exists is null)
            _dbContext.Spifs.Add(entity);

        await _dbContext.SaveChangesAsync(ct);

        return Ok(new
        {
            success = true,
            policyOid = spifIndex.PolicyOid,
            name = spifIndex.PolicyName,
            schemaVersion = entity.SchemaVersion
        });
    }
}
