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
    /// Candidates exist and their repositories are eligible, but every repository with queued
    /// work is at or above its <c>MaxConcurrentWorkers</c> limit. No issue can be claimed on
    /// this tick; the dispatched capacity token will be released via <c>ClaimSkipped</c>.
    /// </summary>
    internal sealed record AllRepositoriesSaturated : SelectionOutcome;
}
