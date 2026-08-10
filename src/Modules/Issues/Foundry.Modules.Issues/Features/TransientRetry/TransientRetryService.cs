using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.TransientRetry;

/// <summary>
/// Periodic background service that re-queues transient failed issues once their backoff has elapsed,
/// up to a maximum of <see cref="MaxTransientRetries"/> consecutive transient attempts.
/// </summary>
internal sealed class TransientRetryService : PeriodicBackgroundService
{
    internal const int MaxTransientRetries = 2;
    internal static readonly TimeSpan InitialBackoff = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TransientRetryService> _log;
    private readonly DateTimeOffset? _nowOverride;

    // Explicit constructor required — PeriodicBackgroundService has a protected constructor,
    // so primary constructors are not available here.
    public TransientRetryService(
        IServiceScopeFactory scopeFactory,
        ILogger<TransientRetryService> logger,
        DateTimeOffset? nowOverride = null) : base(logger)
    {
        _scopeFactory = scopeFactory;
        _log = logger;
        _nowOverride = nowOverride;
    }

    protected override TimeSpan TickInterval => InitialBackoff;

    protected override string ServiceName => nameof(TransientRetryService);

    /// <summary>
    /// Exposes <see cref="TickAsync"/> for direct invocation in unit tests without
    /// spinning up the full <see cref="PeriodicBackgroundService.ExecuteAsync"/> loop.
    /// </summary>
    internal Task TickForTest(CancellationToken cancellationToken) => TickAsync(cancellationToken);

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        IDomainEventDispatcher domainEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();
        IWorkerRunQueries workerRunQueries =
            scope.ServiceProvider.GetRequiredService<IWorkerRunQueries>();

        DateTimeOffset now = _nowOverride ?? DateTimeOffset.UtcNow;

        IReadOnlyList<Issue> candidates = await FindDueTransientFailuresAsync(db, now, cancellationToken);

        foreach (Issue candidate in candidates)
        {
            await TryRetryAsync(db, domainEventDispatcher, workerRunQueries, candidate, now, cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<Issue>> FindDueTransientFailuresAsync(
        DbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Coarse SQL prefilter: candidates whose FailedAt is at least InitialBackoff in the past.
        // Exact per-candidate backoff computation happens in memory via CountConsecutiveTransientRunsAsync.
        DateTimeOffset coarseCutoff = now - InitialBackoff;

        // SQLite stores DateTimeOffset as TEXT; the comparison is evaluated in memory after ToListAsync.
        // Load transient FailedIssue and ContinuableFailedIssue candidates separately by typed set.
        List<FailedIssue> failedCandidates = await db.Set<FailedIssue>()
            .AsNoTracking()
            .Where(i => i.FailureCategory == "transient_api_error")
            .ToListAsync(cancellationToken);

        List<ContinuableFailedIssue> continuableCandidates = await db.Set<ContinuableFailedIssue>()
            .AsNoTracking()
            .Where(i => i.FailureCategory == "transient_api_error")
            .ToListAsync(cancellationToken);

        // Apply coarse cutoff in memory (SQLite DateTimeOffset translation limitation).
        List<Issue> candidates = [
            ..failedCandidates.Where(i => i.FailedAt <= coarseCutoff),
            ..continuableCandidates.Where(i => i.FailedAt <= coarseCutoff),
        ];

        return candidates;
    }

    private async Task TryRetryAsync(
        DbContext db,
        IDomainEventDispatcher domainEventDispatcher,
        IWorkerRunQueries workerRunQueries,
        Issue candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            Guid issueId = candidate.Id.Value;

            if (candidate is not FailedIssue and not ContinuableFailedIssue)
            {
                return;
            }

            DateTimeOffset failedAt = candidate is FailedIssue fi
                ? fi.FailedAt
                : ((ContinuableFailedIssue)candidate).FailedAt;

            int attempt = await workerRunQueries.CountConsecutiveTransientRunsAsync(
                issueId,
                MaxTransientRetries,
                cancellationToken);

            if (attempt >= MaxTransientRetries)
            {
                // Exhausted — let the issue stay failed; manual retry is still available.
                return;
            }

            TimeSpan backoff = ComputeBackoff(attempt);

            if (failedAt + backoff > now)
            {
                // Not yet due.
                return;
            }

            // Reload the issue inside the scope (AsNoTracking was used above) to get a tracked instance.
            Issue? live = await db.Set<Issue>()
                .FirstOrDefaultAsync(i => i.Id == candidate.Id, cancellationToken);

            Issue? next = live switch
            {
                FailedIssue failed => failed.Retry(),
                ContinuableFailedIssue continuableFailed => continuableFailed.Retry(),
                _ => null,
            };

            if (next is null)
            {
                // Issue was concurrently transitioned by a manual retry — skip without error.
                return;
            }

            await db.TransitionAsync(live!, next, domainEventDispatcher, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Concurrent manual retry won the race — no action needed.
        }
#pragma warning disable CA1031 // Per-candidate failure must not abort the entire tick; the error log surfaces the issue without interrupting the loop.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _log.LogWarning(
                ex,
                "Transient auto-retry tick failed for issue {IssueId}.",
                candidate.Id.Value);
        }
    }

    /// <summary>
    /// Computes the exponential backoff for a given attempt count.
    /// <paramref name="attempt"/> is the number of prior consecutive transient runs (0-based):
    /// 0 prior runs = first retry, 1 prior run = second retry. Both use 1-minute backoff;
    /// at 2+ runs (capped by <see cref="MaxTransientRetries"/>) no retry is issued.
    /// </summary>
    internal static TimeSpan ComputeBackoff(int attempt)
    {
        // exponent = max(0, attempt-1): attempt 0 or 1 → exponent 0 → 1min * 2^0 = 1 minute.
        // Doubling applies if MaxTransientRetries > 2 (e.g., attempt 2 → 2min).
        int exponent = Math.Max(0, attempt - 1);
        return TimeSpan.FromTicks(InitialBackoff.Ticks * (1L << exponent));
    }
}
