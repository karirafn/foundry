using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Publishing;

/// <summary>
/// The publishing loop (ADR 0100): discards unpublished runs (failed or blocked) past their Hand-off time to live, starts due publish
/// retries, and publishes every publishing run. A separate loop from dispatch, so a slow push never delays monitoring.
/// A run whose provider host is paused waits, untouched, for the pause to clear before it retries or publishes (#885);
/// the pause never extends its Hand-off time to live.
/// </summary>
internal sealed class RunPublishingService(
    IServiceScopeFactory scopeFactory,
    ILogger<RunPublishingService> logger,
    TimeProvider timeProvider) : PeriodicBackgroundService(logger)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override TimeSpan TickInterval => Interval;

    protected override Task TickAsync(CancellationToken cancellationToken) => ExecuteTickAsync(cancellationToken);

    internal async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        WorkerRunPublisher runPublisher = scope.ServiceProvider.GetRequiredService<WorkerRunPublisher>();
        PublishHostGate hostGate = await PublishHostGate.ReadAsync(
            scope.ServiceProvider.GetRequiredService<IGlobalSettingsQueries>(),
            scope.ServiceProvider.GetRequiredService<IRepositoryDispatchQueries>(),
            cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        // Filtered in memory: SQLite cannot translate DateTimeOffset comparisons.
        List<PublishBlockedRun> blockedRuns = await dbContext.Set<PublishBlockedRun>().ToListAsync(cancellationToken);

        foreach (PublishBlockedRun blocked in blockedRuns.Where(blocked => blocked.DiscardsAt <= now))
        {
            await runPublisher.DiscardAsync(blocked, cancellationToken);
        }

        List<PublishFailedRun> failedRuns = await dbContext.Set<PublishFailedRun>().ToListAsync(cancellationToken);

        foreach (PublishFailedRun failed in failedRuns)
        {
            if (failed.DiscardsAt <= now)
            {
                await runPublisher.DiscardAsync(failed, cancellationToken);
            }
            else if (failed.IsRetryDue(now) && !await hostGate.IsHeldAsync(failed.MonitoredRepositoryId, cancellationToken))
            {
                await runPublisher.RetryAsync(failed, cancellationToken);
            }
        }

        List<PublishingRun> publishingRuns = await dbContext.Set<PublishingRun>().ToListAsync(cancellationToken);

        foreach (PublishingRun run in publishingRuns)
        {
            if (!await hostGate.IsHeldAsync(run.MonitoredRepositoryId, cancellationToken))
            {
                await runPublisher.PublishAsync(run, cancellationToken);
            }
        }
    }
}
