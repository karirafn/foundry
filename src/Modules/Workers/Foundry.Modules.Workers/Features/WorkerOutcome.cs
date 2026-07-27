using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features;

internal abstract record WorkerOutcome
{
    private WorkerOutcome()
    {
    }

    /// <summary>
    /// The merge request is merged and the issue is complete.
    /// </summary>
    internal sealed record Completed(
        BranchName BranchName,
        PullRequestUrl PullRequestUrl,
        RunResultSummary? Summary) : WorkerOutcome;

    /// <summary>
    /// A merge request exists and is open; the issue moves to review.
    /// </summary>
    internal sealed record Review(
        BranchName BranchName,
        PullRequestUrl PullRequestUrl,
        RunResultSummary? Summary) : WorkerOutcome;

    /// <summary>
    /// The run failed but the branch has commits — the issue may be retried from this branch.
    /// </summary>
    internal sealed record ContinuableFailure(
        BranchName BranchName,
        FailureReason FailureReason,
        string? ContainerOutput,
        RunResultSummary? Summary) : WorkerOutcome;

    /// <summary>
    /// The run failed with no recoverable commits.
    /// </summary>
    internal sealed record Failure(
        FailureReason FailureReason,
        string? ContainerOutput,
        RunResultSummary? Summary) : WorkerOutcome;

    /// <summary>
    /// The container exited cleanly with no commits — the issue is unchanged.
    /// </summary>
    internal sealed record Unchanged(BranchName BranchName, RunResultSummary? Summary) : WorkerOutcome;

    /// <summary>
    /// A transient or unknown provider error; the caller should not transition state and may retry.
    /// </summary>
    internal sealed record Indeterminate(Error Error) : WorkerOutcome;
}
