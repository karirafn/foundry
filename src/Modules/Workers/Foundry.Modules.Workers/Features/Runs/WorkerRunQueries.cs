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
                        // Partial sums over runs that carry a result summary: unknown runs are
                        // counted rather than summed, so a partial total is reported as such.
                        InputTokens: TokenUsage.Sum(groupRows
                            .Where(r => r.Tokens is not null)
                            .Select(r => r.Tokens!.BillableInputTokens)),
                        OutputTokens: TokenUsage.Sum(groupRows
                            .Where(r => r.Tokens is not null)
                            .Select(r => r.Tokens!.OutputTokens)));
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

        string? transcript = await db.Set<WorkerRunTranscript>()
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => t.Transcript)
            .FirstOrDefaultAsync(cancellationToken);

        if (transcript is not null)
        {
            return new WorkerRunLogResult.TranscriptAvailable(transcript);
        }

        string? containerOutput = run switch
        {
            CompletedRun completed => completed.ContainerOutput,
            FailedRun failed => failed.ContainerOutput,
            _ => null,
        };

        return string.IsNullOrEmpty(containerOutput)
            ? new WorkerRunLogResult.NoLog()
            : new WorkerRunLogResult.LogAvailable(containerOutput);
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
            WorkerRunId: run.Id,
            IssueId: run.IssueId.Value,
            Role: run.Role.Name,
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
            HasStoredLog: false,
            CancelRequestedAt: run.CancelRequestedAt);

    private static WorkerRunDetail MapCompleted(CompletedRun run) =>
        new(
            WorkerRunId: run.Id,
            IssueId: run.IssueId.Value,
            Role: run.Role.Name,
            State: "completed",
            FailureCategory: null,
            FailureSummary: null,
            ResultText: run.ResultSummary?.ResultText,
            Subtype: run.ResultSummary?.Subtype,
            IsError: run.ResultSummary?.IsError,
            DurationMs: run.ResultSummary?.DurationMs,
            NumTurns: run.ResultSummary?.NumTurns,
            TotalCostUsd: run.ResultSummary?.TotalCostUsd,
            InputTokens: run.ResultSummary?.Tokens.BillableInputTokens,
            OutputTokens: run.ResultSummary?.OutputTokens,
            LastActivityAt: null,
            HasStoredLog: run is { ContainerOutput.Length: > 0 },
            CancelRequestedAt: run.CancelRequestedAt);

    private static WorkerRunDetail MapFailed(FailedRun run) =>
        new(
            WorkerRunId: run.Id,
            IssueId: run.IssueId.Value,
            Role: run.Role.Name,
            State: "failed",
            FailureCategory: run.Reason.CategoryToken,
            FailureSummary: run.Reason.Summary,
            ResultText: run.ResultSummary?.ResultText,
            Subtype: run.ResultSummary?.Subtype,
            IsError: run.ResultSummary?.IsError,
            DurationMs: run.ResultSummary?.DurationMs,
            NumTurns: run.ResultSummary?.NumTurns,
            TotalCostUsd: run.ResultSummary?.TotalCostUsd,
            InputTokens: run.ResultSummary?.Tokens.BillableInputTokens,
            OutputTokens: run.ResultSummary?.OutputTokens,
            LastActivityAt: null,
            HasStoredLog: run is { ContainerOutput.Length: > 0 },
            CancelRequestedAt: run.CancelRequestedAt);

    private static WorkerRunDetail MapStarting(StartingRun run) =>
        new(
            WorkerRunId: run.Id,
            IssueId: run.IssueId.Value,
            Role: run.Role.Name,
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
            HasStoredLog: false,
            CancelRequestedAt: null);

    public async Task<IReadOnlyCollection<WorkerActivity>> GetActiveRunActivityAsync(
        CancellationToken cancellationToken)
    {
        List<WorkerActivity> activities = await db.Set<ActiveRun>()
            .AsNoTracking()
            .Select(r => new WorkerActivity(
                r.Id,
                r.IssueId.Value,
                r.LastActivityAt ?? r.StartedAt,
                r.BranchCommitCount))
            .ToListAsync(cancellationToken);

        return activities;
    }

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
                HasSummary: r.ResultSummary != null,
                InputTokens: r.ResultSummary != null ? r.ResultSummary.InputTokens : null,
                CacheReadInputTokens: r.ResultSummary != null ? r.ResultSummary.CacheReadInputTokens : null,
                CacheCreationInputTokens: r.ResultSummary != null ? r.ResultSummary.CacheCreationInputTokens : null,
                OutputTokens: r.ResultSummary != null ? r.ResultSummary.OutputTokens : null))
            .ToListAsync(cancellationToken);

        List<DateFilteredTelemetryRow> allFailed = await db.Set<FailedRun>()
            .AsNoTracking()
            .Select(r => new DateFilteredTelemetryRow(
                CreatedAt: r.CreatedAt,
                DurationMs: r.ResultSummary != null ? (long?)r.ResultSummary.DurationMs : null,
                NumTurns: r.ResultSummary != null ? (int?)r.ResultSummary.NumTurns : null,
                TotalCostUsd: r.ResultSummary != null ? r.ResultSummary.TotalCostUsd : null,
                HasSummary: r.ResultSummary != null,
                InputTokens: r.ResultSummary != null ? r.ResultSummary.InputTokens : null,
                CacheReadInputTokens: r.ResultSummary != null ? r.ResultSummary.CacheReadInputTokens : null,
                CacheCreationInputTokens: r.ResultSummary != null ? r.ResultSummary.CacheCreationInputTokens : null,
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
                .Select(r => new TelemetryRow(r.DurationMs, r.NumTurns, r.TotalCostUsd, r.Tokens)),
            ..allFailed
                .Where(r => r.CreatedAt >= from)
                .Where(r => r.CreatedAt < to)
                .Select(r => new TelemetryRow(r.DurationMs, r.NumTurns, r.TotalCostUsd, r.Tokens)),
        ];

        int inProgressCount = inProgressDates
            .Count(d => d >= from && d < to);

        int runCount = inWindowRows.Count + inProgressCount;

        List<TokenUsage> inWindowTokens = [..inWindowRows
            .Select(r => r.Tokens)
            .OfType<TokenUsage>()];

        return new RunTotals(
            RunCount: runCount,
            DurationMs: inWindowRows.Sum(r => r.DurationMs) ?? 0L,
            NumTurns: inWindowRows.Sum(r => r.NumTurns) ?? 0,
            TotalCostUsd: inWindowRows.Sum(r => r.TotalCostUsd) ?? 0m,
            // Partial sums: unknown runs are counted rather than summed, so a window containing a
            // run with unknown tokens reports a lower bound with its untracked-run count.
            InputTokens: TokenUsage.Sum(inWindowTokens.Select(t => t.BillableInputTokens)),
            OutputTokens: TokenUsage.Sum(inWindowTokens.Select(t => t.OutputTokens)));
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
                HasSummary: r.ResultSummary != null,
                InputTokens: r.ResultSummary != null ? r.ResultSummary.InputTokens : null,
                CacheReadInputTokens: r.ResultSummary != null ? r.ResultSummary.CacheReadInputTokens : null,
                CacheCreationInputTokens: r.ResultSummary != null ? r.ResultSummary.CacheCreationInputTokens : null,
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
                HasSummary: r.ResultSummary != null,
                InputTokens: r.ResultSummary != null ? r.ResultSummary.InputTokens : null,
                CacheReadInputTokens: r.ResultSummary != null ? r.ResultSummary.CacheReadInputTokens : null,
                CacheCreationInputTokens: r.ResultSummary != null ? r.ResultSummary.CacheCreationInputTokens : null,
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
                HasSummary: false,
                InputTokens: null,
                CacheReadInputTokens: null,
                CacheCreationInputTokens: null,
                OutputTokens: null))
            .ToListAsync(cancellationToken);

        return [..completed, ..failed, ..noTelemetry];
    }

    // Token buckets are stored as int? per-row; TokenUsage widens them to long? so sums across
    // many runs cannot overflow. Tokens is null for a run without a result summary.
    private sealed record WorkerRunTelemetryRow(
        Guid IssueId,
        long? DurationMs,
        int? NumTurns,
        decimal? TotalCostUsd,
        bool HasSummary,
        int? InputTokens,
        int? CacheReadInputTokens,
        int? CacheCreationInputTokens,
        int? OutputTokens)
    {
        public TokenUsage? Tokens => HasSummary
            ? new TokenUsage(InputTokens, CacheReadInputTokens, CacheCreationInputTokens, OutputTokens)
            : null;
    }

    // Per-row projection for GetRunTotalsAsync — no IssueId needed since this is a cross-issue aggregate.
    // Includes CreatedAt for in-memory window filtering (SQLite cannot translate DateTimeOffset >= in LINQ).
    private sealed record DateFilteredTelemetryRow(
        DateTimeOffset CreatedAt,
        long? DurationMs,
        int? NumTurns,
        decimal? TotalCostUsd,
        bool HasSummary,
        int? InputTokens,
        int? CacheReadInputTokens,
        int? CacheCreationInputTokens,
        int? OutputTokens)
    {
        public TokenUsage? Tokens => HasSummary
            ? new TokenUsage(InputTokens, CacheReadInputTokens, CacheCreationInputTokens, OutputTokens)
            : null;
    }

    // Post-filter projection after window filtering has been applied.
    private sealed record TelemetryRow(
        long? DurationMs,
        int? NumTurns,
        decimal? TotalCostUsd,
        TokenUsage? Tokens);

    // Minimal projection for CountConsecutiveTransientRunsAsync.
    // CategoryToken is null for non-FailedRun rows (starting, active, completed);
    // non-null for FailedRun rows.
    private sealed record WorkerRunStreakRow(
        DateTimeOffset CreatedAt,
        string? CategoryToken);
}
