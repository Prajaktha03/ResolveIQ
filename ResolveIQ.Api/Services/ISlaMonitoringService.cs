namespace ResolveIQ.Api.Services;

public interface ISlaMonitoringService
{
    Task<int> EvaluateDeadlinesAsync(CancellationToken cancellationToken = default);
}
