using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using BranchNameValue = Foundry.Shared.BranchName;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class RevisionQueuedIssue : QueuedIssue
{
    // Private parameterless constructor for EF Core materialization.
    private RevisionQueuedIssue()
    {
    }

    private RevisionQueuedIssue(IssueId id) : base(id)
    {
    }

    public override int TierRank => 0;

    public override BranchNameValue DispatchBranchName => BranchNameValue.From(BranchName);

    public override DispatchContext Context =>
        new DispatchContext.Revision(BranchName, PullRequestUrl, Trigger);

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public RevisionTrigger Trigger { get; private set; } = new RevisionTrigger.ReviewFeedback([], 0, null);

    public int ChecksFixAttempts { get; private set; }

    internal static RevisionQueuedIssue FromReview(
        ReviewIssue source,
        IReadOnlyList<ReviewComment> comments,
        int omittedCommentCount = 0,
        DateTimeOffset? newestCommentAt = null)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id);
        revisionQueued.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.ReviewFeedback(comments, omittedCommentCount, newestCommentAt);
        revisionQueued.ChecksFixAttempts = 0;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromChecksPending(
        ChecksPendingIssue source,
        IReadOnlyList<ReviewComment> comments,
        int omittedCommentCount = 0,
        DateTimeOffset? newestCommentAt = null)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id);
        revisionQueued.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.ReviewFeedback(comments, omittedCommentCount, newestCommentAt);
        revisionQueued.ChecksFixAttempts = 0;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromChecksPendingFix(
        ChecksPendingIssue source,
        IReadOnlyList<FailingCheck> failingChecks)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id);
        revisionQueued.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = new RevisionTrigger.FailedChecks(failingChecks);
        revisionQueued.ChecksFixAttempts = source.ChecksFixAttempts + 1;
        return revisionQueued;
    }

    internal static RevisionQueuedIssue FromRevisionFailed(RevisionFailedIssue source)
    {
        RevisionQueuedIssue revisionQueued = new(source.Id);
        revisionQueued.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        revisionQueued.BranchName = source.BranchName;
        revisionQueued.PullRequestUrl = source.PullRequestUrl;
        revisionQueued.Trigger = source.Trigger;
        revisionQueued.ChecksFixAttempts = source.ChecksFixAttempts;
        return revisionQueued;
    }

    public override RevisionInProgressIssue Claim(WorkerRunId workerRunId)
    {
        RevisionInProgressIssue revisionInProgress = RevisionInProgressIssue.FromRevisionQueued(this, workerRunId);
        AddDomainEvent(new Events.IssueRevisionInProgress(Id, MonitoredRepositoryId));
        return revisionInProgress;
    }
}
