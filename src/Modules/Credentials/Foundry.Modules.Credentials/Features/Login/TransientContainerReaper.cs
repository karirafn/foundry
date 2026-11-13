using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// Periodic background service that reaps orphaned transient containers (login, credential-helper
/// and Publisher) left over from crashes or unexpected exits. Runs every 60 seconds.
/// </summary>
/// <remarks>
/// Filters by <c>foundry.transient=true</c> — never by <c>foundry.managed</c>, which worker
/// containers also carry. Only non-running containers (exited/dead/created) created longer than
/// <see cref="ReapGracePeriod"/> ago are reaped; running containers are skipped because they may be
/// the active login session. Any running crash orphan is already bounded by the
/// <c>timeout -k 10</c> wrapper and <c>AutoRemove</c>.
/// </remarks>
internal sealed class TransientContainerReaper(
    ICredentialsOrchestrator orchestrator,
    TimeProvider timeProvider,
    ILogger<TransientContainerReaper> logger) : PeriodicBackgroundService(logger)
{
    // A Publisher's owner creates the container, starts it, waits for its exit and then reads its
    // logs, for up to the run's 15-minute prepare or publish timeout. A younger non-running
    // container may be created-but-not-started or exited-but-not-read, so it may still have a live
    // reader; only an older one is a crash orphan.
    private static readonly TimeSpan ReapGracePeriod = TimeSpan.FromMinutes(30);

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
            DateTimeOffset createdBefore = timeProvider.GetUtcNow() - ReapGracePeriod;
            IReadOnlyList<string> containers =
                await orchestrator.ListExitedTransientContainersAsync(createdBefore, cancellationToken);

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
