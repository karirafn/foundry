using Foundry.Modules.Credentials.Infrastructure;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// Startup hook that stops and removes any orphaned <c>foundry.login</c> containers
/// left over from a previous Foundry instance (crash, restart, etc.).
/// Runs as part of <see cref="IHostedLifecycleService.StartingAsync"/> so it completes
/// before the application starts accepting requests.
/// </summary>
internal sealed class LoginContainerReaper(
    ICredentialsOrchestrator orchestrator,
    ILogger<LoginContainerReaper> logger) : IHostedService, IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> orphans =
            await orchestrator.ListTransientContainersAsync(cancellationToken);

        if (orphans.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "Reaping {Count} orphaned foundry.login container(s) from previous session.",
            orphans.Count);

        foreach (string containerId in orphans)
        {
            await ReapContainerAsync(containerId, cancellationToken);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ReapContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await orchestrator.StopContainerAsync(containerId, cancellationToken);
            await orchestrator.RemoveContainerAsync(containerId, cancellationToken);

            logger.LogInformation(
                "Reaped orphaned foundry.login container {ContainerId}.",
                containerId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to reap orphaned foundry.login container {ContainerId}.",
                containerId);
        }
    }
}
