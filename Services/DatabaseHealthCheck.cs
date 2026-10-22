using AriesMagicAppointmentSystem.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AriesMagicAppointmentSystem.Services;

public sealed class DatabaseHealthCheck(ApplicationDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database unavailable.");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("Database unavailable.");
        }
    }
}
