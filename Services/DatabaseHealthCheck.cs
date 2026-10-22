using AriesMagicAppointmentSystem.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AriesMagicAppointmentSystem.Services;

public sealed class DatabaseHealthCheck(ApplicationDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await database.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("Database unavailable.");
            return (await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any()
                ? HealthCheckResult.Unhealthy("Database schema requires migration.")
                : HealthCheckResult.Healthy();
        }
        catch
        {
            return HealthCheckResult.Unhealthy("Database unavailable.");
        }
    }
}
