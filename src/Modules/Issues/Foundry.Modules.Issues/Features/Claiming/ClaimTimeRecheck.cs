using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.Dependencies;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.Claiming;

/// <summary>
/// Re-checks a queued candidate against the provider just before it is claimed. The recorded BlockedBy may predate a
/// blocker added on the provider (GitLab sends no webhook for issue links), so the open blockers are re-read live; a
/// candidate that gained one is transitioned to <see cref="BlockedIssue"/> or <see cref="TriageBlockedIssue"/>.
/// </summary>
internal sealed class ClaimTimeRecheck(
    IIssueBlockerQueries issueBlockerQueries,
    IssueDependencyApplier dependencyApplier,
    ILogger<ClaimTimeRecheck> logger)
{
    public async Task<ClaimTimeRecheckOutcome> CheckAsync(
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
}

/// <summary>What <see cref="ClaimTimeRecheck"/> found for one candidate.</summary>
internal enum ClaimTimeRecheckOutcome
{
    /// <summary>Nothing changed on the provider: the candidate may be claimed.</summary>
    Clear,

    /// <summary>The candidate gained an open blocker and has left the queue.</summary>
    Blocked,

    /// <summary>The provider read failed: the candidate stays queued for the next dispatch tick.</summary>
    ReadFailed,
}
