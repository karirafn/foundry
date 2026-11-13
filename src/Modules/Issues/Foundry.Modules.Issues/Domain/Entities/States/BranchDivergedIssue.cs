using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// The Publisher pushed the run's work to <see cref="SideBranchName"/> instead of the issue branch, for
/// <see cref="DivergenceReason"/>: someone pushed to the issue branch while the run worked and the work could not join it
/// (ADR 0100), or the provider refused the lease push of a rebased tip because the branch is protected from force pushes
/// (ADR 0101). The issue waits for a person to bring the side branch into the issue branch; it resumes on its own once
/// the issue branch contains it, or through Retry Issue.
/// </summary>
public sealed class BranchDivergedIssue : Issue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => true;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private BranchDivergedIssue()
    {
    }

    private BranchDivergedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string SideBranchName { get; private set; } = string.Empty;

    public BranchDivergenceReason DivergenceReason { get; private set; }

    public string PullRequestUrl { get; private set; } = string.Empty;

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    public bool FromRevision { get; private set; }

    internal static BranchDivergedIssue FromInProgress(
        InProgressIssue source,
        string branchName,
        string sideBranchName,
        BranchDivergenceReason divergenceReason,
        string? pullRequestUrl,
        DateTimeOffset transitionedAt)
    {
        BranchDivergedIssue diverged = new(source.Id, transitionedAt);
        diverged.CopySharedPropertiesFrom(source);
        diverged.WorkerRunId = source.WorkerRunId;
        diverged.BranchName = branchName;
        diverged.SideBranchName = sideBranchName;
        diverged.DivergenceReason = divergenceReason;
        diverged.PullRequestUrl = pullRequestUrl ?? string.Empty;
        return diverged;
    }

    internal static BranchDivergedIssue FromRevisionInProgress(
        RevisionInProgressIssue source,
        string sideBranchName,
        BranchDivergenceReason divergenceReason,
        DateTimeOffset transitionedAt)
    {
        BranchDivergedIssue diverged = new(source.Id, transitionedAt);
        diverged.CopySharedPropertiesFrom(source);
        diverged.WorkerRunId = source.WorkerRunId;
        diverged.BranchName = source.BranchName;
        diverged.SideBranchName = sideBranchName;
        diverged.DivergenceReason = divergenceReason;
        diverged.PullRequestUrl = source.PullRequestUrl;
        diverged.FromRevision = true;
        return diverged;
    }

    /// <summary>
    /// Continues on the issue branch. Used both by Retry Issue and when the side branch has been merged into the issue
    /// branch.
    /// </summary>
    public ContinuationQueuedIssue Retry(DateTimeOffset transitionedAt)
    {
        ContinuationQueuedIssue queued = ContinuationQueuedIssue.FromBranchDiverged(this, transitionedAt);
        AddDomainEvent(new Events.IssueContinuationQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
