using AbacController.Api.Runtime;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AbacController.Api.Health;

/// <summary>
/// Health check that reports healthy once application startup is complete.
/// </summary>
public sealed class StartupHealthCheck(AppRuntimeState runtimeState) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(runtimeState.StartupCompleted
            ? HealthCheckResult.Healthy("Startup completed.")
            : HealthCheckResult.Unhealthy("Startup still in progress."));
    }
}
