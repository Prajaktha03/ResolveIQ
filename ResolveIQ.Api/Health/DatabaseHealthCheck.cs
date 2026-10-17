using Microsoft.Extensions.Diagnostics.HealthChecks;
using ResolveIQ.Api.Data;

namespace ResolveIQ.Api.Health;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(AppDbContext db, ILogger<DatabaseHealthCheck> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
            if (canConnect)
            {
                var data = new Dictionary<string, object>
                {
                    ["database"] = "Operational"
                };
                return HealthCheckResult.Healthy("Database is responsive.", data);
            }

            var failedData = new Dictionary<string, object>
            {
                ["database"] = "Unreachable"
            };
            return HealthCheckResult.Unhealthy("Cannot connect to database.", data: failedData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed.");
            var errorData = new Dictionary<string, object>
            {
                ["database"] = "Error"
            };
            return HealthCheckResult.Unhealthy("Database health check encountered an error.", data: errorData);
        }
    }
}
