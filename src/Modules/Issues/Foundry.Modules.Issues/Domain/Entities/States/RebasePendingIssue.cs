using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// A worker run left an open pull request and Foundry is rebasing its branch onto the default branch
/// through the provider (ADR 0087). The issue leaves this state for checks-pending once the rebase step
/// resolves a <see cref="RebaseOutcome"/>, so no checks verdict is ever read against the pre-rebase head.
/// </summary>
public sealed class RebasePendingIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private RebasePendingIssue()
    {
    }

    private RebasePendingIssue(IssueId id) : base(id)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public DateTimeOffset EnteredRebasePendingAt { get; private set; }

    public DateTimeOffset FeedbackCutoffAt { get; private set; }

    public int ChecksFixAttempts { get; private set; }

    internal static RebasePendingIssue FromInProgress(
        InProgressIssue source,
        string branchName,
        string pullRequestUrl,
        DateTimeOffset feedbackCutoffAt)
    {
        RebasePendingIssue rebasePending = new(source.Id);
        rebasePending.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        rebasePending.WorkerRunId = source.WorkerRunId;
        rebasePending.BranchName = branchName;
        rebasePending.PullRequestUrl = pullRequestUrl;
        rebasePending.EnteredRebasePendingAt = DateTimeOffset.UtcNow;
        rebasePending.FeedbackCutoffAt = feedbackCutoffAt;
        rebasePending.ChecksFixAttempts = 0;
        return rebasePending;
    }

    internal static RebasePendingIssue FromRevisionInProgress(
        RevisionInProgressIssue source,
        DateTimeOffset feedbackCutoffAt)
    {
        RebasePendingIssue rebasePending = new(source.Id);
        rebasePending.SetSharedProperties(
            source.MonitoredRepositoryId,
            source.IssueNumber,
            source.Title,
            source.Author,
            source.Url,
            source.Labels,
            source.DetectedAt);
        rebasePending.WorkerRunId = source.WorkerRunId;
        rebasePending.BranchName = source.BranchName;
        rebasePending.PullRequestUrl = source.PullRequestUrl;
        rebasePending.EnteredRebasePendingAt = DateTimeOffset.UtcNow;
        rebasePending.FeedbackCutoffAt = feedbackCutoffAt;
        rebasePending.ChecksFixAttempts = source.ChecksFixAttempts;
        return rebasePending;
    }

    /// <summary>
    /// The rebase step resolved with <paramref name="rebaseOutcome"/>: the issue moves on to the checks gate,
    /// which now reads checks against the (possibly rebased) head.
    /// </summary>
    public ChecksPendingIssue MarkChecksPending(RebaseOutcome rebaseOutcome)
    {
        ChecksPendingIssue checksPending = ChecksPendingIssue.FromRebasePending(this, rebaseOutcome);
        AddDomainEvent(new Events.IssueChecksPending(
            Id,
            MonitoredRepositoryId,
            checksPending.IssueNumber,
            checksPending.EnteredChecksPendingAt));
        return checksPending;
    }

    public CompletedIssue Complete(DateTimeOffset completedAt)
    {
        CompletedIssue completed = CompletedIssue.FromRebasePending(this, completedAt);
        AddDomainEvent(new Events.IssueCompleted(Id, MonitoredRepositoryId));
        return completed;
    }

    public ContinuableFailedIssue Fail(
        string failureReason,
        FailureCategoryVO failureCategory,
        DateTimeOffset failedAt)
    {
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromRebasePending(
            this,
            failureReason,
            failureCategory,
            failedAt);
        AddDomainEvent(new Events.IssueContinuableFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
