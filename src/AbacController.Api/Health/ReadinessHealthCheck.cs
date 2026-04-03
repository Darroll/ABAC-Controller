using AbacController.Api.Runtime;
using AbacController.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AbacController.Api.Health;

public sealed class ReadinessHealthCheck(
    AppRuntimeState runtimeState,
    AbacDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!runtimeState.StartupCompleted)
        {
            return HealthCheckResult.Unhealthy("Startup incomplete.");
        }

        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        return canConnect
            ? HealthCheckResult.Healthy("Database connection available.")
            : HealthCheckResult.Unhealthy("Database connection unavailable.");
    }
}
