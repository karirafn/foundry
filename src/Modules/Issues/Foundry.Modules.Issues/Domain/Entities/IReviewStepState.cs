using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities;

/// <summary>
/// A state of the review step (ADR 0113): the issue's pull request passed its checks gate and the review role is
/// judging it at <see cref="ReviewedCommit"/>. Every such state carries what the gate resolved, so the verdict, a
/// failure or a closure can route the issue on without reading the gate again.
/// </summary>
public interface IReviewStepState
{
    /// <summary>The issue's latest worker run: the run that last pushed, or the review run once one is claimed.</summary>
    WorkerRunId WorkerRunId { get; }

    string BranchName { get; }

    string PullRequestUrl { get; }

    DateTimeOffset FeedbackCutoffAt { get; }

    /// <summary>The checks gate's outcome: <see cref="ChecksOutcome.Passed"/> or <see cref="ChecksOutcome.NoneDetected"/>.</summary>
    ChecksOutcome ChecksOutcome { get; }

    RebaseOutcome? RebaseOutcome { get; }

    int RebaseConflictAttempts { get; }

    /// <summary>The pull request's head commit the checks gate verified, which the reviewer judges.</summary>
    string ReviewedCommit { get; }

    /// <summary>
    /// The repository's Merge When Checks Pass setting when the checks gate resolved (ADR 0105); an approval merges
    /// only when it is on.
    /// </summary>
    bool AutoMerge { get; }
}
