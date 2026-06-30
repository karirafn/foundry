using System.Diagnostics;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features;

internal sealed class WorkerRunQueries(DbContext db) : IWorkerRunQueries
{
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
            CommitMarkers: run.CommitMarkers
                .Select(m => new WorkerRunCommitMarker(m.ObservedAt, m.Sha, m.Message))
                .ToList(),
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
}
