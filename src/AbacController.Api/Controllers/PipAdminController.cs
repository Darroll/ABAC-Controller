using AbacController.Data;
using AbacController.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Api.Controllers;

/// <summary>
/// PIP (Policy Information Point) administration endpoints for managing attribute sources.
/// </summary>
[ApiController]
[Route("api/v1/pip")]
public sealed class PipAdminController : ControllerBase
{
    private readonly AbacDbContext _dbContext;

    public PipAdminController(AbacDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("sources")]
    [Authorize(Policy = "PipRead")]
    public async Task<ActionResult<List<PipSourceEntity>>> ListSources(CancellationToken ct)
        => Ok(await _dbContext.PipSources.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct));

    [HttpGet("sources/{id}")]
    [Authorize(Policy = "PipRead")]
    public async Task<ActionResult<PipSourceEntity>> GetSource(string id, CancellationToken ct)
    {
        var source = await _dbContext.PipSources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return source is null ? NotFound() : Ok(source);
    }

    [HttpPut("sources/{id}")]
    [Authorize(Policy = "PipAdmin")]
    public async Task<ActionResult<PipSourceEntity>> UpsertSource(string id, [FromBody] PipSourceEntity source, CancellationToken ct)
    {
        if (!string.Equals(id, source.Id, StringComparison.Ordinal))
            return BadRequest("Route id must match source.id");

        var existing = await _dbContext.PipSources.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (existing is null)
        {
            source.CreatedAt = DateTimeOffset.UtcNow;
            source.UpdatedAt = DateTimeOffset.UtcNow;
            _dbContext.PipSources.Add(source);
            await _dbContext.SaveChangesAsync(ct);
            return Ok(source);
        }

        existing.Name = source.Name;
        existing.SourceType = source.SourceType;
        existing.ConfigJson = source.ConfigJson;
        existing.ProvidesAttributes = source.ProvidesAttributes;
        existing.Priority = source.Priority;
        existing.IsRequired = source.IsRequired;
        existing.CacheEnabled = source.CacheEnabled;
        existing.CacheTtlSeconds = source.CacheTtlSeconds;
        existing.CacheMaxEntries = source.CacheMaxEntries;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(ct);
        return Ok(existing);
    }

    [HttpDelete("sources/{id}")]
    [Authorize(Policy = "PipAdmin")]
    public async Task<IActionResult> DeleteSource(string id, CancellationToken ct)
    {
        var source = await _dbContext.PipSources.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (source is null)
            return NotFound();

        _dbContext.PipSources.Remove(source);
        await _dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("sources/{id}/test")]
    [Authorize(Policy = "PipAdmin")]
    public async Task<ActionResult<object>> TestSource(string id, CancellationToken ct)
    {
        var source = await _dbContext.PipSources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (source is null)
            return NotFound();

        return Ok(new
        {
            source.Id,
            source.SourceType,
            healthy = false,
            message = "Connectivity test endpoint present; runtime connector-specific active test not wired for persisted sources yet."
        });
    }
}
