using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class InProgressIssue : Issue
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private InProgressIssue()
    {
    }

    private InProgressIssue(IssueId id) : base(id)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    internal static InProgressIssue FromQueued(FreshQueuedIssue queued, WorkerRunId workerRunId)
    {
        InProgressIssue inProgress = new(queued.Id);
        inProgress.CopySharedPropertiesFrom(queued);
        inProgress.WorkerRunId = workerRunId;
        return inProgress;
    }

    public ReviewIssue MarkInReview(
        string branchName,
        string pullRequestUrl,
        DateTimeOffset feedbackCutoffAt)
    {
        ReviewIssue review = ReviewIssue.FromInProgress(this, branchName, pullRequestUrl, feedbackCutoffAt);
        AddDomainEvent(new Events.IssueInReview(Id, MonitoredRepositoryId));
        return review;
    }

    /// <summary>
    /// The run left an open pull request: Foundry rebases its branch onto the default branch before the
    /// checks gate reads any verdict (ADR 0087).
    /// </summary>
    public RebasePendingIssue MarkRebasePending(
        string branchName,
        string pullRequestUrl,
        DateTimeOffset feedbackCutoffAt)
    {
        RebasePendingIssue rebasePending = RebasePendingIssue.FromInProgress(
            this,
            branchName,
            pullRequestUrl,
            feedbackCutoffAt);
        AddDomainEvent(new Events.IssueRebasePending(
            Id,
            MonitoredRepositoryId,
            rebasePending.IssueNumber,
            rebasePending.EnteredRebasePendingAt));
        return rebasePending;
    }

    /// <summary>
    /// The run stopped to ask a person (ADR 0096). The resume target comes from what the run left: its open pull
    /// request, its commits, or nothing.
    /// </summary>
    public AwaitingInputIssue MarkAwaitingInput(
        string branchName,
        string? pullRequestUrl,
        bool branchHasCommits,
        IReadOnlyList<InputQuestion> questions,
        DateTimeOffset askedAt)
    {
        AwaitingInputIssue awaiting = AwaitingInputIssue.FromInProgress(
            this,
            ResumeTarget.FromRunFacts(branchName, pullRequestUrl, branchHasCommits),
            questions,
            askedAt);
        AddDomainEvent(new Events.IssueAwaitingInput(Id, MonitoredRepositoryId, IssueNumber));
        return awaiting;
    }

    public UnchangedIssue MarkUnchanged()
    {
        UnchangedIssue unchanged = UnchangedIssue.FromInProgress(this);
        AddDomainEvent(new Events.IssueUnchanged(Id, MonitoredRepositoryId));
        return unchanged;
    }

    public CompletedIssue MarkCompleted(
        string branchName,
        string pullRequestUrl,
        DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromInProgress(this, branchName, pullRequestUrl, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    public FailedIssue MarkFailed(
        string failureReason,
        DateTimeOffset failedAt,
        FailureCategory failureCategory)
    {
        FailedIssue failed = FailedIssue.FromInProgress(this, failureReason, failureCategory, failedAt);
        AddDomainEvent(new Events.IssueFailed(Id, MonitoredRepositoryId));
        return failed;
    }

    public ContinuableFailedIssue MarkContinuableFailed(
        string branchName,
        string failureReason,
        FailureCategory failureCategory,
        DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromInProgress(
            this,
            branchName,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }

    /// <summary>
    /// The run's publish retries ran out (ADR 0100); its unpublished work waits for Retry publish.
    /// </summary>
    public PublishFailedIssue MarkPublishFailed(string branchName, string failureReason, DateTimeOffset failedAt)
    {
        PublishFailedIssue publishFailed = PublishFailedIssue.FromInProgress(this, branchName, failureReason, failedAt);
        AddDomainEvent(new Events.IssuePublishFailed(Id, MonitoredRepositoryId));
        return publishFailed;
    }

    /// <summary>
    /// The Diff gate found possible secrets in the run's commits, so nothing was pushed (#788); the unpublished work
    /// waits for an operator to discard it.
    /// </summary>
    public PublishBlockedIssue MarkPublishBlocked(string branchName, DateTimeOffset blockedAt)
    {
        PublishBlockedIssue blocked = PublishBlockedIssue.FromInProgress(this, branchName, blockedAt);
        AddDomainEvent(new Events.IssuePublishBlocked(Id, MonitoredRepositoryId));
        return blocked;
    }

    /// <summary>
    /// The run's work could not be merged into the branch someone pushed to during the run, so it went to
    /// <paramref name="sideBranchName"/> (ADR 0100).
    /// </summary>
    public BranchDivergedIssue MarkBranchDiverged(string branchName, string sideBranchName, string? pullRequestUrl)
    {
        BranchDivergedIssue diverged = BranchDivergedIssue.FromInProgress(this, branchName, sideBranchName, pullRequestUrl);
        AddDomainEvent(new Events.IssueBranchDiverged(Id, MonitoredRepositoryId));
        return diverged;
    }

    internal static InProgressIssue FromPublishFailed(PublishFailedIssue source)
    {
        InProgressIssue inProgress = new(source.Id);
        inProgress.CopySharedPropertiesFrom(source);
        inProgress.WorkerRunId = source.WorkerRunId;
        return inProgress;
    }

    internal static InProgressIssue FromContinuationQueued(ContinuationQueuedIssue source, WorkerRunId workerRunId)
    {
        InProgressIssue inProgress = new(source.Id);
        inProgress.CopySharedPropertiesFrom(source);
        inProgress.WorkerRunId = workerRunId;
        return inProgress;
    }
}
