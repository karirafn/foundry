using Foundry.Modules.Credentials.Infrastructure;
using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// Periodic background service that reaps orphaned transient containers (login and credential-helper)
/// left over from crashes or unexpected exits. Runs every 60 seconds.
/// </summary>
/// <remarks>
/// Filters by <c>foundry.transient=true</c> — never by <c>foundry.managed</c>, which worker
/// containers also carry. Self-removing containers (<c>AutoRemove=true</c>) that have already
/// exited are not listed, so stop/remove calls are only issued to genuinely orphaned ones.
/// </remarks>
internal sealed class TransientContainerReaper(
    ICredentialsOrchestrator orchestrator,
    ILogger<TransientContainerReaper> logger) : PeriodicBackgroundService(logger)
{
    protected override TimeSpan TickInterval => TimeSpan.FromSeconds(60);

    protected override string ServiceName => nameof(TransientContainerReaper);

    /// <summary>
    /// Exposes <see cref="TickAsync"/> for direct invocation in unit tests without
    /// spinning up the full <see cref="PeriodicBackgroundService.ExecuteAsync"/> loop.
    /// </summary>
    internal Task TickForTest(CancellationToken cancellationToken) => TickAsync(cancellationToken);

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<string> containers =
                await orchestrator.ListTransientContainersAsync(cancellationToken);

            foreach (string containerId in containers)
            {
                await ReapContainerAsync(containerId, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Transient container reap tick failed.");
        }
    }

    private async Task ReapContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await orchestrator.StopContainerAsync(containerId, cancellationToken);
            await orchestrator.RemoveContainerAsync(containerId, cancellationToken);

            logger.LogInformation("Reaped orphaned transient container {ContainerId}.", containerId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to reap orphaned transient container {ContainerId}.",
                containerId);
        }
    }
}
