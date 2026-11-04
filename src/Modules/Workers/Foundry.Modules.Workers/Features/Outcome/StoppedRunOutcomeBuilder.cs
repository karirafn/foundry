using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// Builds the <see cref="WorkerOutcome"/> for a run whose container Foundry stopped itself
/// (timeout or user cancellation). Consults
/// <see cref="IPostExitProviderQueries.GetBranchCommitSummaryAsync"/> to decide between
/// <see cref="WorkerOutcome.ContinuableFailure"/> (commits exist or the query failed transiently)
/// and <see cref="WorkerOutcome.Failure"/> (the branch definitively has no commits).
/// </summary>
internal static class StoppedRunOutcomeBuilder
{
    public static async Task<WorkerOutcome> BuildAsync(
        ActiveRun run,
        FailureReason failureReason,
        string? containerOutput,
        RunResultSummary? summary,
        IPostExitProviderQueries postExitProviderQueries,
        CancellationToken cancellationToken)
    {
        Result<BranchCommitSummary> commitsResult = await postExitProviderQueries.GetBranchCommitSummaryAsync(
            run.MonitoredRepositoryId,
            run.BranchName.Value,
            cancellationToken);

        if (commitsResult is Result<BranchCommitSummary>.Failure commitsFailure)
        {
            if (commitsFailure.Error.Kind == ErrorKind.NotFound)
            {
                // Branch definitively does not exist — no commits to preserve
                return new WorkerOutcome.Failure(failureReason, containerOutput, summary);
            }

            // Transient provider failure — preserve the branch to allow a future retry
            return new WorkerOutcome.ContinuableFailure(run.BranchName, failureReason, containerOutput, summary);
        }

        bool hasCommits = ((Result<BranchCommitSummary>.Success)commitsResult).Value.CommitCount > 0;

        return hasCommits
            ? new WorkerOutcome.ContinuableFailure(run.BranchName, failureReason, containerOutput, summary)
            : new WorkerOutcome.Failure(failureReason, containerOutput, summary);
    }
}
