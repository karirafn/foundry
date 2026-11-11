using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Publishing;

/// <summary>
/// The publishing loop (ADR 0100): discards unpublished runs past their Hand-off time to live, starts due publish
/// retries, and publishes every publishing run. A separate loop from dispatch, so a slow push never delays monitoring.
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
        DateTimeOffset now = timeProvider.GetUtcNow();

        // Filtered in memory: SQLite cannot translate DateTimeOffset comparisons.
        List<PublishFailedRun> failedRuns = await dbContext.Set<PublishFailedRun>().ToListAsync(cancellationToken);

        foreach (PublishFailedRun failed in failedRuns)
        {
            if (failed.DiscardsAt <= now)
            {
                await runPublisher.DiscardAsync(failed, cancellationToken);
            }
            else if (failed.IsRetryDue(now))
            {
                await runPublisher.RetryAsync(failed, cancellationToken);
            }
        }

        List<PublishingRun> publishingRuns = await dbContext.Set<PublishingRun>().ToListAsync(cancellationToken);

        foreach (PublishingRun run in publishingRuns)
        {
            await runPublisher.PublishAsync(run, cancellationToken);
        }
    }
}
