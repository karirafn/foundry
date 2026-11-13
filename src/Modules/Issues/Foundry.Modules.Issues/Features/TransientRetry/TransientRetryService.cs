using System.Diagnostics;

using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.TransientRetry;

/// <summary>
/// Periodic background service that re-queues failed issues whose <see cref="FailureCategory"/> retries
/// automatically, once the category's <see cref="AutomaticRetrySchedule"/> backoff has elapsed and until
/// its maximum streak of consecutive auto-retryable runs is reached.
/// </summary>
internal sealed class TransientRetryService : PeriodicBackgroundService
{
    private static readonly IReadOnlyList<FailureCategory> AutoRetryableCategories = [.. FailureCategory.AutoRetryable];

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

    // Ticking at the shortest first backoff keeps a retry late by at most one backoff of the fastest schedule.
    protected override TimeSpan TickInterval => FailureCategory.ShortestFirstAutomaticRetryBackoff;

    protected override string ServiceName => nameof(TransientRetryService);

    /// <summary>
    /// Exposes <see cref="TickAsync"/> for direct invocation in unit tests without
    /// spinning up the full <see cref="PeriodicBackgroundService.ExecuteAsync"/> loop.
    /// </summary>
    internal Task TickForTest(CancellationToken cancellationToken) => TickAsync(cancellationToken);

    /// <summary>
    /// The auto-retryable failed-issue query for one failed state, exposed so an integration test can
    /// confirm EF translates the category filter to SQL.
    /// </summary>
    internal static IQueryable<TIssue> WhereAutoRetryable<TIssue>(IQueryable<TIssue> issues)
        where TIssue : Issue, IAutoRetryableFailedState =>
        issues.Where(i => AutoRetryableCategories.Contains(i.FailureCategory));

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
        // FailedAt is applied as a coarse in-memory cutoff — the SQLite EF provider cannot translate
        // DateTimeOffset comparisons server-side (stored as ISO 8601 TEXT). Exact per-candidate backoff
        // computation happens in memory against the candidate's own category schedule.
        DateTimeOffset coarseCutoff = now - FailureCategory.ShortestFirstAutomaticRetryBackoff;

        List<Issue> candidates = [
            ..await LoadAsync<FailedIssue>(db, cancellationToken),
            ..await LoadAsync<ContinuableFailedIssue>(db, cancellationToken),
            ..await LoadAsync<TriageFailedIssue>(db, cancellationToken),
            ..await LoadAsync<RevisionFailedIssue>(db, cancellationToken),
        ];

        return [.. candidates.Where(i => ((IAutoRetryableFailedState)i).FailedAt <= coarseCutoff)];
    }

    private static async Task<List<TIssue>> LoadAsync<TIssue>(DbContext db, CancellationToken cancellationToken)
        where TIssue : Issue, IAutoRetryableFailedState =>
        await WhereAutoRetryable(db.Set<TIssue>().AsNoTracking()).ToListAsync(cancellationToken);

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
            IAutoRetryableFailedState failure = (IAutoRetryableFailedState)candidate;

            // The latest failure's category decides the schedule for the whole streak.
            AutomaticRetrySchedule schedule = failure.FailureCategory.AutomaticRetry
                ?? throw new UnreachableException(
                    $"Candidate {candidate.Id.Value} has non-retryable category {failure.FailureCategory}.");

            int streak = await workerRunQueries.CountConsecutiveTransientRunsAsync(
                candidate.Id.Value,
                schedule.MaxRetries,
                cancellationToken);

            if (schedule.IsExhausted(streak))
            {
                // Exhausted — let the issue stay failed; manual retry is still available.
                return;
            }

            if (failure.FailedAt + schedule.BackoffAfter(streak) > now)
            {
                // Not yet due.
                return;
            }

            // Reload the issue inside the scope (AsNoTracking was used above) to get a tracked instance.
            Issue? live = await db.Set<Issue>()
                .FirstOrDefaultAsync(i => i.Id == candidate.Id, cancellationToken);

            if (live is not IAutoRetryableFailedState)
            {
                // Issue was concurrently transitioned by a manual retry — skip without error.
                return;
            }

            Issue next = live switch
            {
                FailedIssue failed => failed.Retry(now),
                ContinuableFailedIssue continuableFailed => continuableFailed.Retry(now),
                // A transient triage failure retries triage, never skips it (ADR 0097).
                TriageFailedIssue triageFailed => triageFailed.RetryTriage(now),
                RevisionFailedIssue revisionFailed => revisionFailed.Retry(now),
                _ => throw new UnreachableException($"Unexpected issue state after guard: {live.GetType().Name}"),
            };

            await db.TransitionAsync(live, next, domainEventDispatcher, cancellationToken);
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
}
