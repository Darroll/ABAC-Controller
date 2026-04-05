using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Entities;
using AbacController.Pip;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Api.Controllers;

/// <summary>
/// Exposes PIP administration endpoints for source management, health reporting, and cache control.
/// </summary>
[ApiController]
[Route("pip/api")]
public sealed class PipAdminController : ControllerBase
{
    private readonly AbacDbContext _dbContext;
    private readonly PipHealthMonitor _healthMonitor;

    /// <summary>Initializes a new instance of the <see cref="PipAdminController"/> class.</summary>
    public PipAdminController(AbacDbContext dbContext, PipHealthMonitor healthMonitor)
    {
        _dbContext = dbContext;
        _healthMonitor = healthMonitor;
    }

    /// <summary>Lists all registered PIP sources.</summary>
    [HttpGet("sources")]
    [Authorize(Policy = "PipRead")]
    public async Task<ActionResult<List<PipSourceEntity>>> ListSources(CancellationToken ct)
        => Ok(await _dbContext.PipSources.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct));

    /// <summary>Gets a PIP source by identifier.</summary>
    [HttpGet("sources/{id}")]
    [Authorize(Policy = "PipRead")]
    public async Task<ActionResult<PipSourceEntity>> GetSource(string id, CancellationToken ct)
    {
        var source = await _dbContext.PipSources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return source is null ? NotFound() : Ok(source);
    }

    /// <summary>Creates a new PIP source configuration or updates an existing one.</summary>
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

    /// <summary>Deletes a PIP source.</summary>
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

    /// <summary>Runs a connectivity test for a specific persisted PIP source.</summary>
    [HttpPost("sources/{id}/test")]
    [Authorize(Policy = "PipAdmin")]
    public async Task<ActionResult<object>> TestSource(string id, CancellationToken ct)
    {
        var source = await _dbContext.PipSources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (source is null)
            return NotFound();

        var status = await _healthMonitor.CheckSourceAsync(id, ct);
        if (status is null)
            return NotFound();

        return Ok(new
        {
            id = status.SourceId,
            sourceType = status.SourceType,
            healthy = status.Healthy,
            message = status.Message,
            responseTimeMs = status.ResponseTime?.TotalMilliseconds,
            lastChecked = status.LastChecked,
            providesAttributes = status.ProvidesAttributes
        });
    }

    /// <summary>Checks the health of all registered PIP sources.</summary>
    [HttpGet("health")]
    [Authorize(Policy = "PipRead")]
    public async Task<ActionResult<object>> CheckHealth(CancellationToken ct)
    {
        var statuses = await _healthMonitor.CheckAllAsync(ct);
        var allHealthy = statuses.All(static s => s.Healthy);

        return Ok(new
        {
            allHealthy,
            sourceCount = statuses.Count,
            sources = statuses
        });
    }

    /// <summary>Returns the last cached health status for all PIP sources without running a new check.</summary>
    [HttpGet("health/cached")]
    [Authorize(Policy = "PipRead")]
    public ActionResult<object> GetCachedHealth()
    {
        var statuses = _healthMonitor.GetStatuses();
        var allHealthy = statuses.All(static s => s.Healthy);

        return Ok(new
        {
            allHealthy,
            sourceCount = statuses.Count,
            sources = statuses
        });
    }

    /// <summary>Invalidates the PIP attribute cache for a specific subject identifier.</summary>
    [HttpPost("cache/invalidate/{subjectId}")]
    [Authorize(Policy = "PipAdmin")]
    public ActionResult InvalidateCache(string subjectId, [FromServices] IPipCacheManager cacheManager)
    {
        cacheManager.Invalidate(subjectId);
        return NoContent();
    }

    /// <summary>Invalidates the entire PIP attribute cache.</summary>
    [HttpPost("cache/invalidate-all")]
    [Authorize(Policy = "PipAdmin")]
    public ActionResult InvalidateAllCache([FromServices] IPipCacheManager cacheManager)
    {
        cacheManager.InvalidateAll();
        return NoContent();
    }
}
