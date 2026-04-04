using AbacController.Api.Configuration;
using AbacController.Api.Runtime;
using AbacController.Core.Interfaces;
using AbacController.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
public sealed class SystemAdminController : ControllerBase
{
    private readonly AppRuntimeState _runtimeState;
    private readonly ILabelCodecRegistry _labelCodecRegistry;
    private readonly ISpifRegistry _spifRegistry;
    private readonly AbacDbContext _dbContext;
    private readonly AbacControllerOptions _options;

    public SystemAdminController(
        AppRuntimeState runtimeState,
        ILabelCodecRegistry labelCodecRegistry,
        ISpifRegistry spifRegistry,
        AbacDbContext dbContext,
        AbacControllerOptions options)
    {
        _runtimeState = runtimeState;
        _labelCodecRegistry = labelCodecRegistry;
        _spifRegistry = spifRegistry;
        _dbContext = dbContext;
        _options = options;
    }

    [HttpGet("info")]
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

    [HttpGet("enforcement-points")]
    [Authorize(Policy = "SysRead")]
    public async Task<ActionResult<object>> ListEnforcementPoints(CancellationToken ct)
        => Ok(await _dbContext.EnforcementPoints.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct));
}
