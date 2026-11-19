using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Siculingo.Infrastructure.Persistence;

public sealed class DatabaseHealthCheck(
    SiculingoDbContext db,
    ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Database.CanConnectAsync(cancellationToken))
        {
            logger.LogWarning("Database readiness failed: connection unavailable.");
            return HealthCheckResult.Unhealthy("Database connection unavailable.");
        }

        if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            logger.LogWarning("Database readiness failed: unapplied migrations.");
            return HealthCheckResult.Unhealthy("Database migrations required.");
        }

        return HealthCheckResult.Healthy();
    }
}
