namespace Foundry.Modules.Issues.Features.Claiming;

/// <summary>
/// Closed union representing the outcome of <see cref="DispatchCandidateSelector.SelectAsync"/>.
/// </summary>
internal abstract record SelectionOutcome
{
    private SelectionOutcome()
    {
    }

    /// <summary>A candidate was found and its repository dispatch info resolved successfully.</summary>
    internal sealed record Selected(DispatchCandidate Candidate) : SelectionOutcome;

    /// <summary>Claimable issues exist but none of their repositories are eligible for dispatch at this tick.</summary>
    internal sealed record NoEligibleRepositories : SelectionOutcome;

    /// <summary>No claimable issues exist.</summary>
    internal sealed record NoCandidates : SelectionOutcome;

    /// <summary>
    /// Candidates exist but every candidate's repository dispatch info failed to resolve.
    /// </summary>
    /// <param name="Skipped">The number of candidates that were skipped.</param>
    internal sealed record AllCandidatesUnresolvable(int Skipped) : SelectionOutcome;

    /// <summary>
    /// Candidates exist but none could be claimed: each was skipped because its repository
    /// dispatch info failed to resolve, it gained an open blocker since the last dependency pass
    /// (and was transitioned to <c>BlockedIssue</c> or <c>TriageBlockedIssue</c>), its branch already had work and it was
    /// requeued as a <c>ContinuationQueuedIssue</c>, or its claim-time provider read failed. At least one candidate was
    /// blocked, adopted or unreadable; when every candidate is unresolvable, <see cref="AllCandidatesUnresolvable"/> is
    /// returned instead.
    /// </summary>
    /// <param name="Unresolvable">Candidates skipped because their dispatch info failed to resolve.</param>
    /// <param name="Blocked">Candidates blocked by an open blocker found at claim time.</param>
    /// <param name="ReadFailed">Candidates skipped because their blocker or branch read failed.</param>
    /// <param name="BranchAdopted">Fresh candidates requeued as a Continuation on their existing branch.</param>
    internal sealed record AllCandidatesSkipped(int Unresolvable, int Blocked, int ReadFailed, int BranchAdopted)
        : SelectionOutcome;

    /// <summary>
    /// Candidates exist and their repositories are eligible, but every repository with queued
    /// work is at or above its <c>MaxConcurrentWorkers</c> limit. No issue can be claimed on
    /// this tick; the dispatched capacity token will be released via <c>ClaimSkipped</c>.
    /// </summary>
    internal sealed record AllRepositoriesSaturated : SelectionOutcome;
}
