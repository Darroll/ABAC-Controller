using AbacController.Api.Configuration;
using AbacController.Api.Runtime;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Api.Controllers;

/// <summary>
/// System administration endpoints for status, configuration, and enforcement point management.
/// </summary>
[ApiController]
[Route("api/v1/system")]
public sealed class SystemAdminController : ControllerBase
{
    private readonly AppRuntimeState _runtimeState;
    private readonly ILabelCodecRegistry _labelCodecRegistry;
    private readonly ISpifRegistry _spifRegistry;
    private readonly AbacDbContext _dbContext;
    private readonly AbacControllerOptions _options;
    private readonly IAuditReader _auditReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemAdminController"/> class.
    /// </summary>
    public SystemAdminController(
        AppRuntimeState runtimeState,
        ILabelCodecRegistry labelCodecRegistry,
        ISpifRegistry spifRegistry,
        AbacDbContext dbContext,
        AbacControllerOptions options,
        IAuditReader auditReader)
    {
        _runtimeState = runtimeState;
        _labelCodecRegistry = labelCodecRegistry;
        _spifRegistry = spifRegistry;
        _dbContext = dbContext;
        _options = options;
        _auditReader = auditReader;
    }

    /// <summary>
    /// Get system status information.
    /// </summary>
    [HttpGet("info")]
    [HttpGet("status")]
    [Authorize(Policy = "SysRead")]
    public ActionResult<object> GetStatus()
        => Ok(new
        {
            startupCompleted = _runtimeState.StartupCompleted,
            ready = _runtimeState.StartupCompleted,
            runtime = "net10.0",
            registeredLabelCodecs = _labelCodecRegistry.GetRegisteredCodecIds(),
            registeredPolicyOids = _spifRegistry.GetRegisteredPolicyOids()
        });

    [HttpGet("config")]
    [Authorize(Policy = "SysAdmin")]
    public ActionResult<object> GetConfig()
        => Ok(new
        {
            databaseProvider = _options.Database.Provider,
            audience = _options.Auth.Audience,
            requireHttpsMetadata = _options.Auth.RequireHttpsMetadata,
            decisionCacheEnabled = _options.Pdp.DecisionCacheEnabled,
            decisionCacheTtlSeconds = _options.Pdp.DecisionCacheTtlSeconds,
            rateLimit = new { _options.RateLimiting.PdpPermitLimit, _options.RateLimiting.WindowSeconds }
        });

    /// <summary>
    /// List registered enforcement points.
    /// </summary>
    [HttpGet("enforcement-points")]
    [Authorize(Policy = "SysRead")]
    public async Task<ActionResult<object>> ListEnforcementPoints(CancellationToken ct)
        => Ok(await _dbContext.EnforcementPoints.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct));

    /// <summary>
    /// Query all audit events with filtering and pagination.
    /// Unlike the PAP audit endpoint which only shows policy changes,
    /// this returns all event types (evaluations, policy changes, system events, etc.).
    /// </summary>
    [HttpGet("audit")]
    [Authorize(Policy = "AuditRead")]
    public async Task<ActionResult<AuditQueryResult>> GetAuditLog(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? eventType,
        [FromQuery] string? subjectId,
        [FromQuery] string? resourceId,
        [FromQuery] string? decision,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var query = new AuditQuery
        {
            From = from,
            To = to,
            EventType = eventType,
            SubjectId = subjectId,
            ResourceId = resourceId,
            Decision = decision,
            Page = page,
            PageSize = Math.Clamp(pageSize, 1, 200)
        };

        var result = await _auditReader.QueryAsync(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get a single audit event by ID.
    /// </summary>
    [HttpGet("audit/{id:guid}")]
    [Authorize(Policy = "AuditRead")]
    public async Task<ActionResult<AuditEvent>> GetAuditEvent(Guid id, CancellationToken ct)
    {
        var evt = await _auditReader.GetByIdAsync(id, ct);
        return evt is null ? NotFound() : Ok(evt);
    }
}
