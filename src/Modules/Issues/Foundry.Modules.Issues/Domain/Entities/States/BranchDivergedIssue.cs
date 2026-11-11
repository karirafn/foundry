using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// Someone pushed to the issue branch while the run worked, and merging the run's work into it conflicted, so the
/// Publisher pushed the run's work to <see cref="SideBranchName"/> instead (ADR 0100). The issue waits for a person to
/// merge the side branch; it resumes on its own once the merge lands, or through Retry Issue.
/// </summary>
public sealed class BranchDivergedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private BranchDivergedIssue()
    {
    }

    private BranchDivergedIssue(IssueId id) : base(id)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string SideBranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    public bool FromRevision { get; private set; }

    internal static BranchDivergedIssue FromInProgress(
        InProgressIssue source,
        string branchName,
        string sideBranchName,
        string? pullRequestUrl)
    {
        BranchDivergedIssue diverged = new(source.Id);
        diverged.CopySharedPropertiesFrom(source);
        diverged.WorkerRunId = source.WorkerRunId;
        diverged.BranchName = branchName;
        diverged.SideBranchName = sideBranchName;
        diverged.PullRequestUrl = pullRequestUrl ?? string.Empty;
        return diverged;
    }

    internal static BranchDivergedIssue FromRevisionInProgress(RevisionInProgressIssue source, string sideBranchName)
    {
        BranchDivergedIssue diverged = new(source.Id);
        diverged.CopySharedPropertiesFrom(source);
        diverged.WorkerRunId = source.WorkerRunId;
        diverged.BranchName = source.BranchName;
        diverged.SideBranchName = sideBranchName;
        diverged.PullRequestUrl = source.PullRequestUrl;
        diverged.FromRevision = true;
        return diverged;
    }

    /// <summary>
    /// Continues on the issue branch. Used both by Retry Issue and when the side branch has been merged into the issue
    /// branch.
    /// </summary>
    public ContinuationQueuedIssue Retry()
    {
        ContinuationQueuedIssue queued = ContinuationQueuedIssue.FromBranchDiverged(this);
        AddDomainEvent(new Events.IssueContinuationQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
