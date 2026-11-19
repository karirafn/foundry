using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.Dependencies;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.Claiming;

/// <summary>
/// Re-checks a queued candidate against the provider just before it is claimed. The recorded BlockedBy may predate a
/// blocker added on the provider (GitLab sends no webhook for issue links), so the open blockers are re-read live; a
/// candidate that gained one is transitioned to <see cref="BlockedIssue"/> or <see cref="TriageBlockedIssue"/>.
/// A <see cref="FreshQueuedIssue"/> is also checked for existing work: Foundry's choice between Fresh and Continuation
/// comes from its own records, which can be lost while the issue's branch survives on the provider. When that branch
/// carries commits the default branch lacks, the candidate is adopted as a <see cref="ContinuationQueuedIssue"/> on it,
/// so the run continues that work instead of re-implementing the issue and diverging from it at publish.
/// </summary>
internal sealed class ClaimTimeRecheck(
    IIssueBlockerQueries issueBlockerQueries,
    IssueDependencyApplier dependencyApplier,
    IPostExitProviderQueries branchQueries,
    FreshBranchAdopter branchAdopter,
    ILogger<ClaimTimeRecheck> logger)
{
    public async Task<ClaimTimeRecheckOutcome> CheckAsync(
        QueuedIssue candidate,
        CancellationToken cancellationToken)
    {
        ClaimTimeRecheckOutcome blockerOutcome = await CheckBlockersAsync(candidate, cancellationToken);

        if (blockerOutcome != ClaimTimeRecheckOutcome.Clear || candidate is not FreshQueuedIssue fresh)
        {
            return blockerOutcome;
        }

        return await CheckBranchWorkAsync(fresh, cancellationToken);
    }

    private async Task<ClaimTimeRecheckOutcome> CheckBlockersAsync(
        QueuedIssue candidate,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<int>> blockers = await issueBlockerQueries.GetOpenBlockersAsync(
            candidate.MonitoredRepositoryId,
            candidate.IssueNumber,
            cancellationToken);

        if (blockers is not Result<IReadOnlyList<int>>.Success blockersSuccess)
        {
            Error error = ((Result<IReadOnlyList<int>>.Failure)blockers).Error;
            logger.LogWarning(
                "Skipping issue #{IssueNumber} for this dispatch: its open blockers could not be read ({ErrorCode}).",
                candidate.IssueNumber,
                error.Code);
            return ClaimTimeRecheckOutcome.ReadFailed;
        }

        if (blockersSuccess.Value.Count == 0)
        {
            return ClaimTimeRecheckOutcome.Clear;
        }

        // Transition to BlockedIssue or TriageBlockedIssue (committed) so the issue is not re-selected on the next tick
        // or on redelivery of this capacity event.
        await dependencyApplier.ApplyAsync(candidate, blockersSuccess.Value, cancellationToken);
        logger.LogInformation(
            "Issue #{IssueNumber} gained open blocker(s) {BlockedBy} since the last dependency pass; blocked instead of claimed.",
            candidate.IssueNumber,
            blockersSuccess.Value);
        return ClaimTimeRecheckOutcome.Blocked;
    }

    private async Task<ClaimTimeRecheckOutcome> CheckBranchWorkAsync(
        FreshQueuedIssue candidate,
        CancellationToken cancellationToken)
    {
        string branchName = candidate.GeneratedBranchName.Value;
        Result<BranchCommitSummary> summary = await branchQueries.GetBranchCommitSummaryAsync(
            candidate.MonitoredRepositoryId,
            branchName,
            cancellationToken);

        if (summary is Result<BranchCommitSummary>.Failure failure)
        {
            if (failure.Error.Kind == ErrorKind.NotFound)
            {
                // The branch does not exist, so there is no earlier work to continue.
                return ClaimTimeRecheckOutcome.Clear;
            }

            // Never fall back to Fresh on an unknown answer: a Fresh run over an existing branch diverges at publish.
            logger.LogWarning(
                "Skipping issue #{IssueNumber} for this dispatch: its branch {BranchName} could not be read ({ErrorCode}).",
                candidate.IssueNumber,
                branchName,
                failure.Error.Code);
            return ClaimTimeRecheckOutcome.ReadFailed;
        }

        int commitCount = ((Result<BranchCommitSummary>.Success)summary).Value.CommitCount;

        if (commitCount == 0)
        {
            return ClaimTimeRecheckOutcome.Clear;
        }

        await branchAdopter.AdoptAsync(candidate, cancellationToken);
        logger.LogInformation(
            "Issue #{IssueNumber} was queued Fresh, but its branch {BranchName} already has {CommitCount} commit(s) not on the default branch; queued as a Continuation on that branch instead.",
            candidate.IssueNumber,
            branchName,
            commitCount);
        return ClaimTimeRecheckOutcome.BranchHasWork;
    }
}

/// <summary>What <see cref="ClaimTimeRecheck"/> found for one candidate.</summary>
internal enum ClaimTimeRecheckOutcome
{
    /// <summary>Nothing changed on the provider: the candidate may be claimed.</summary>
    Clear,

    /// <summary>The candidate gained an open blocker and has left the queue.</summary>
    Blocked,

    /// <summary>
    /// The Fresh candidate's branch already has commits the default branch lacks: it was requeued as a Continuation on
    /// that branch and has left the Fresh queue.
    /// </summary>
    BranchHasWork,

    /// <summary>The provider read failed: the candidate stays queued for the next dispatch tick.</summary>
    ReadFailed,
}
