using Docker.DotNet;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Foundry.Modules.Workers.Features.Health;

internal sealed class DockerDaemonHealthCheck(
    ISystemOperations systemOperations,
    TimeSpan timeout) : IHealthCheck
{
    public const string CheckName = "docker-daemon";

    private const string UnhealthyMessage = "Docker daemon connectivity check failed.";

    /// <summary>
    /// Initializes a new <see cref="DockerDaemonHealthCheck"/> with the production default 3-second timeout.
    /// </summary>
    public DockerDaemonHealthCheck(ISystemOperations systemOperations)
        : this(systemOperations, TimeSpan.FromSeconds(3))
    {
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeoutCts = new(timeout);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        try
        {
            await systemOperations.PingAsync(linkedCts.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The linked timeout fired — not a host-shutdown signal.
            return HealthCheckResult.Unhealthy(UnhealthyMessage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(UnhealthyMessage, ex);
        }
    }
}
