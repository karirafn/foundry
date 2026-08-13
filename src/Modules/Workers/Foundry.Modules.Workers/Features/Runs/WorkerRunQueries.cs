using System.Diagnostics;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Runs;

internal sealed class WorkerRunQueries(DbContext db) : IWorkerRunQueries
{
    public async Task<IReadOnlyDictionary<Guid, RunAggregate>> GetRunAggregatesForIssuesAsync(
        IReadOnlyCollection<Guid> issueIds,
        CancellationToken cancellationToken)
    {
        if (issueIds.Count == 0)
        {
            return new Dictionary<Guid, RunAggregate>();
        }

        List<IssueId> issueIdValues = issueIds
            .Select(IssueId.From)
            .ToList();

        // The telemetry columns live on the RunResultSummary owned entity, configured via
        // OwnsOne on CompletedRun and FailedRun only. EF cannot project OwnsOne properties
        // from the base TPH Set<WorkerRun>() because the owned navigation is not on the base.
        // Query the base set for per-row IssueId + telemetry-bearing subtype detection,
        // then aggregate in memory. A single round-trip fetches all rows for the given issue IDs.
        List<WorkerRunTelemetryRow> allRows = await FetchTelemetryRowsAsync(issueIdValues, cancellationToken);

        return allRows
            .GroupBy(r => r.IssueId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    List<WorkerRunTelemetryRow> groupRows = g.ToList();
                    return new RunAggregate(
                        RunCount: groupRows.Count,
                        DurationMs: groupRows.Any(r => r.DurationMs.HasValue)
                            ? groupRows.Sum(r => r.DurationMs)
                            : null,
                        NumTurns: groupRows.Any(r => r.NumTurns.HasValue)
                            ? groupRows.Sum(r => r.NumTurns)
                            : null,
                        TotalCostUsd: groupRows.Any(r => r.TotalCostUsd.HasValue)
                            ? groupRows.Sum(r => r.TotalCostUsd)
                            : null,
                        // int? per-row tokens promote to long? in the aggregate to avoid overflow.
                        InputTokens: groupRows.Any(r => r.InputTokens.HasValue)
                            ? groupRows.Sum(r => (long?)r.InputTokens)
                            : null,
                        OutputTokens: groupRows.Any(r => r.OutputTokens.HasValue)
                            ? groupRows.Sum(r => (long?)r.OutputTokens)
                            : null);
                });
    }

    public async Task<Result<WorkerRunDetail>> GetWorkerRunDetailAsync(
        Guid workerRunId,
        CancellationToken cancellationToken)
    {
        WorkerRunId id = WorkerRunId.From(workerRunId);

        WorkerRun? run = await db.Set<WorkerRun>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (run is null)
        {
            return Result<WorkerRunDetail>.Fail(WorkerRunErrors.NotFound(id));
        }

        return MapToDetail(run);
    }

    public async Task<WorkerRunLogResult> GetWorkerRunLogAsync(
        Guid workerRunId,
        CancellationToken cancellationToken)
    {
        WorkerRunId id = WorkerRunId.From(workerRunId);

        WorkerRun? run = await db.Set<WorkerRun>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (run is null)
        {
            return new WorkerRunLogResult.RunNotFound();
        }

        if (run is not FailedRun { ContainerOutput.Length: > 0 } failedRun)
        {
            return new WorkerRunLogResult.NoLog();
        }

        return new WorkerRunLogResult.LogAvailable(failedRun.ContainerOutput);
    }

    private static WorkerRunDetail MapToDetail(WorkerRun run) =>
        run switch
        {
            ActiveRun active => MapActive(active),
            CompletedRun completed => MapCompleted(completed),
            FailedRun failed => MapFailed(failed),
            StartingRun starting => MapStarting(starting),
            _ => throw new UnreachableException($"Unknown {nameof(WorkerRun)} subtype: {run.GetType().Name}"),
        };

    private static WorkerRunDetail MapActive(ActiveRun run) =>
        new(
            WorkerRunId: run.Id.Value,
            IssueId: run.IssueId.Value,
            State: "running",
            FailureCategory: null,
            FailureSummary: null,
            ResultText: null,
            Subtype: null,
            IsError: null,
            DurationMs: null,
            NumTurns: null,
            TotalCostUsd: null,
            InputTokens: null,
            OutputTokens: null,
            LastActivityAt: run.LastActivityAt,
            CommitMarkers: [],
            HasStoredLog: false);

    private static WorkerRunDetail MapCompleted(CompletedRun run) =>
        new(
            WorkerRunId: run.Id.Value,
            IssueId: run.IssueId.Value,
            State: "completed",
            FailureCategory: null,
            FailureSummary: null,
            ResultText: run.ResultSummary?.ResultText,
            Subtype: run.ResultSummary?.Subtype,
            IsError: run.ResultSummary?.IsError,
            DurationMs: run.ResultSummary?.DurationMs,
            NumTurns: run.ResultSummary?.NumTurns,
            TotalCostUsd: run.ResultSummary?.TotalCostUsd,
            InputTokens: run.ResultSummary?.InputTokens,
            OutputTokens: run.ResultSummary?.OutputTokens,
            LastActivityAt: null,
            CommitMarkers: [],
            HasStoredLog: false);

    private static WorkerRunDetail MapFailed(FailedRun run) =>
        new(
            WorkerRunId: run.Id.Value,
            IssueId: run.IssueId.Value,
            State: "failed",
            FailureCategory: run.Reason.CategoryToken,
            FailureSummary: run.Reason.Summary,
            ResultText: run.ResultSummary?.ResultText,
            Subtype: run.ResultSummary?.Subtype,
            IsError: run.ResultSummary?.IsError,
            DurationMs: run.ResultSummary?.DurationMs,
            NumTurns: run.ResultSummary?.NumTurns,
            TotalCostUsd: run.ResultSummary?.TotalCostUsd,
            InputTokens: run.ResultSummary?.InputTokens,
            OutputTokens: run.ResultSummary?.OutputTokens,
            LastActivityAt: null,
            CommitMarkers: [],
            HasStoredLog: run is { ContainerOutput.Length: > 0 });

    private static WorkerRunDetail MapStarting(StartingRun run) =>
        new(
            WorkerRunId: run.Id.Value,
            IssueId: run.IssueId.Value,
            State: "starting",
            FailureCategory: null,
            FailureSummary: null,
            ResultText: null,
            Subtype: null,
            IsError: null,
            DurationMs: null,
            NumTurns: null,
            TotalCostUsd: null,
            InputTokens: null,
            OutputTokens: null,
            LastActivityAt: null,
            CommitMarkers: [],
            HasStoredLog: false);

    public async Task<int> CountConsecutiveTransientRunsAsync(
        Guid issueId,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        IssueId id = IssueId.From(issueId);

        // The SQLite provider stores DateTimeOffset as ISO 8601 TEXT and throws
        // NotSupportedException for DateTimeOffset in ORDER BY clauses, so ordering and
        // Take() must be applied in memory. To bound the materialized payload, two typed-set
        // queries are used rather than one base-type query:
        //   - Set<FailedRun>: projects (created_at, reason.CategoryToken) — reason is on
        //     FailedRun only; EF fetches just created_at and reason, applies the ValueConverter
        //     to deserialize reason client-side, and the Select projection skips the OwnsOne
        //     ResultSummary columns (no full-entity hydration).
        //   - Set<WorkerRun> excluding FailedRun: projects (created_at, null) — only the
        //     timestamp is needed for ordering; no payload fields are fetched.
        //
        // A streak breaks on the first row that is not a transient failed run — including
        // CompletedRun, StartingRun, or ActiveRun — so all row types must be included.
        List<WorkerRunStreakRow> failedRows = await db.Set<FailedRun>()
            .AsNoTracking()
            .Where(r => r.IssueId == id)
            .Select(r => new WorkerRunStreakRow(r.CreatedAt, r.Reason.CategoryToken))
            .ToListAsync(cancellationToken);

        List<WorkerRunStreakRow> otherRows = await db.Set<WorkerRun>()
            .AsNoTracking()
            .Where(r => r.IssueId == id)
            .Where(r => !(r is FailedRun))
            .Select(r => new WorkerRunStreakRow(r.CreatedAt, null))
            .ToListAsync(cancellationToken);

        List<WorkerRunStreakRow> recent = [..Enumerable.Concat(failedRows, otherRows)
            .OrderByDescending(r => r.CreatedAt)
            .Take(maxAttempts + 1)];

        int count = 0;

        foreach (WorkerRunStreakRow row in recent)
        {
            if (row.CategoryToken == FailureReason.TransientApiErrorToken)
            {
                count++;
            }
            else
            {
                break;
            }
        }

        return count;
    }

    public async Task<RunTotals> GetRunTotalsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        // The SQLite provider stores DateTimeOffset as ISO 8601 TEXT. EF Core 10 cannot translate
        // DateTimeOffset comparison operators (>=, <) in server-side LINQ for this mapping — attempting
        // to push the CreatedAt window filter into a WHERE clause throws InvalidOperationException at
        // query compilation time. The CreatedAt-to-SQL pre-filter path was considered and is not viable
        // on the SQLite provider. This matches the sibling GetRunAggregatesForIssuesAsync in-memory
        // pattern and is acceptable at the current POC scale (issue management, not analytics).
        //
        // The telemetry columns live on the RunResultSummary owned entity, configured via
        // OwnsOne on CompletedRun and FailedRun only. Query each typed set separately so EF
        // can project the owned navigation, then apply the window filter in memory.
        List<DateFilteredTelemetryRow> allCompleted = await db.Set<CompletedRun>()
            .AsNoTracking()
            .Select(r => new DateFilteredTelemetryRow(
                CreatedAt: r.CreatedAt,
                DurationMs: r.ResultSummary != null ? (long?)r.ResultSummary.DurationMs : null,
                NumTurns: r.ResultSummary != null ? (int?)r.ResultSummary.NumTurns : null,
                TotalCostUsd: r.ResultSummary != null ? r.ResultSummary.TotalCostUsd : null,
                InputTokens: r.ResultSummary != null ? r.ResultSummary.InputTokens : null,
                OutputTokens: r.ResultSummary != null ? r.ResultSummary.OutputTokens : null))
            .ToListAsync(cancellationToken);

        List<DateFilteredTelemetryRow> allFailed = await db.Set<FailedRun>()
            .AsNoTracking()
            .Select(r => new DateFilteredTelemetryRow(
                CreatedAt: r.CreatedAt,
                DurationMs: r.ResultSummary != null ? (long?)r.ResultSummary.DurationMs : null,
                NumTurns: r.ResultSummary != null ? (int?)r.ResultSummary.NumTurns : null,
                TotalCostUsd: r.ResultSummary != null ? r.ResultSummary.TotalCostUsd : null,
                InputTokens: r.ResultSummary != null ? r.ResultSummary.InputTokens : null,
                OutputTokens: r.ResultSummary != null ? r.ResultSummary.OutputTokens : null))
            .ToListAsync(cancellationToken);

        // StartingRun and ActiveRun contribute to RunCount but have no telemetry.
        List<DateTimeOffset> inProgressDates = await db.Set<WorkerRun>()
            .AsNoTracking()
            .Where(r => r is StartingRun || r is ActiveRun)
            .Select(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        List<TelemetryRow> inWindowRows = [
            ..allCompleted
                .Where(r => r.CreatedAt >= from)
                .Where(r => r.CreatedAt < to)
                .Select(r => new TelemetryRow(r.DurationMs, r.NumTurns, r.TotalCostUsd, r.InputTokens, r.OutputTokens)),
            ..allFailed
                .Where(r => r.CreatedAt >= from)
                .Where(r => r.CreatedAt < to)
                .Select(r => new TelemetryRow(r.DurationMs, r.NumTurns, r.TotalCostUsd, r.InputTokens, r.OutputTokens)),
        ];

        int inProgressCount = inProgressDates
            .Count(d => d >= from && d < to);

        int runCount = inWindowRows.Count + inProgressCount;

        return new RunTotals(
            RunCount: runCount,
            DurationMs: inWindowRows.Sum(r => r.DurationMs) ?? 0L,
            NumTurns: inWindowRows.Sum(r => r.NumTurns) ?? 0,
            TotalCostUsd: inWindowRows.Sum(r => r.TotalCostUsd) ?? 0m,
            // int? per-row tokens promote to long? via cast to avoid integer overflow.
            InputTokens: inWindowRows.Sum(r => (long?)r.InputTokens) ?? 0L,
            OutputTokens: inWindowRows.Sum(r => (long?)r.OutputTokens) ?? 0L);
    }

    // Fetches one row per worker run for the specified issues. Server-side filtering (WHERE issue_id IN ...)
    // is applied to keep the result set small; per-row telemetry is extracted from the typed subtypes
    // because EF cannot project OwnsOne navigation properties from the base Set<WorkerRun>() in TPH.
    private async Task<List<WorkerRunTelemetryRow>> FetchTelemetryRowsAsync(
        List<IssueId> issueIds,
        CancellationToken cancellationToken)
    {
        List<WorkerRunTelemetryRow> completed = await db.Set<CompletedRun>()
            .AsNoTracking()
            .Where(r => issueIds.Contains(r.IssueId))
            .Select(r => new WorkerRunTelemetryRow(
                IssueId: r.IssueId.Value,
                DurationMs: r.ResultSummary != null ? (long?)r.ResultSummary.DurationMs : null,
                NumTurns: r.ResultSummary != null ? (int?)r.ResultSummary.NumTurns : null,
                TotalCostUsd: r.ResultSummary != null ? r.ResultSummary.TotalCostUsd : null,
                InputTokens: r.ResultSummary != null ? r.ResultSummary.InputTokens : null,
                OutputTokens: r.ResultSummary != null ? r.ResultSummary.OutputTokens : null))
            .ToListAsync(cancellationToken);

        List<WorkerRunTelemetryRow> failed = await db.Set<FailedRun>()
            .AsNoTracking()
            .Where(r => issueIds.Contains(r.IssueId))
            .Select(r => new WorkerRunTelemetryRow(
                IssueId: r.IssueId.Value,
                DurationMs: r.ResultSummary != null ? (long?)r.ResultSummary.DurationMs : null,
                NumTurns: r.ResultSummary != null ? (int?)r.ResultSummary.NumTurns : null,
                TotalCostUsd: r.ResultSummary != null ? r.ResultSummary.TotalCostUsd : null,
                InputTokens: r.ResultSummary != null ? r.ResultSummary.InputTokens : null,
                OutputTokens: r.ResultSummary != null ? r.ResultSummary.OutputTokens : null))
            .ToListAsync(cancellationToken);

        // StartingRun and ActiveRun contribute to RunCount but have no telemetry.
        List<WorkerRunTelemetryRow> noTelemetry = await db.Set<WorkerRun>()
            .AsNoTracking()
            .Where(r => issueIds.Contains(r.IssueId))
            .Where(r => r is StartingRun || r is ActiveRun)
            .Select(r => new WorkerRunTelemetryRow(
                IssueId: r.IssueId.Value,
                DurationMs: null,
                NumTurns: null,
                TotalCostUsd: null,
                InputTokens: null,
                OutputTokens: null))
            .ToListAsync(cancellationToken);

        return [..completed, ..failed, ..noTelemetry];
    }

    // InputTokens and OutputTokens are stored as int? per-row but aggregated as long? to avoid
    // integer overflow when summing across many runs with high token counts.
    private sealed record WorkerRunTelemetryRow(
        Guid IssueId,
        long? DurationMs,
        int? NumTurns,
        decimal? TotalCostUsd,
        int? InputTokens,
        int? OutputTokens);

    // Per-row projection for GetRunTotalsAsync — no IssueId needed since this is a cross-issue aggregate.
    // Includes CreatedAt for in-memory window filtering (SQLite cannot translate DateTimeOffset >= in LINQ).
    private sealed record DateFilteredTelemetryRow(
        DateTimeOffset CreatedAt,
        long? DurationMs,
        int? NumTurns,
        decimal? TotalCostUsd,
        int? InputTokens,
        int? OutputTokens);

    // Post-filter projection after window filtering has been applied.
    private sealed record TelemetryRow(
        long? DurationMs,
        int? NumTurns,
        decimal? TotalCostUsd,
        int? InputTokens,
        int? OutputTokens);

    // Minimal projection for CountConsecutiveTransientRunsAsync.
    // CategoryToken is null for non-FailedRun rows (starting, active, completed);
    // non-null for FailedRun rows.
    private sealed record WorkerRunStreakRow(
        DateTimeOffset CreatedAt,
        string? CategoryToken);
}
