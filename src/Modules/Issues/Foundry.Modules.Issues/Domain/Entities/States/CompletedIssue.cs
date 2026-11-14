using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class CompletedIssue : Issue
{
    public override IssueStage Stage => IssueStage.Done;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private CompletedIssue()
    {
    }

    private CompletedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public string BranchName { get; private set; } = string.Empty;

    public string PullRequestUrl { get; private set; } = string.Empty;

    public override string? LinkedPullRequestUrl => NullIfEmpty(PullRequestUrl);

    public DateTimeOffset CompletedAt { get; private set; }

    internal static CompletedIssue FromReview(ReviewIssue source, DateTimeOffset completedAt)
    {
        CompletedIssue completed = new(source.Id, completedAt);
        completed.CopySharedPropertiesFrom(source);
        completed.BranchName = source.BranchName;
        completed.PullRequestUrl = source.PullRequestUrl;
        completed.CompletedAt = completedAt;
        return completed;
    }

    internal static CompletedIssue FromRebasePending(RebasePendingIssue source, DateTimeOffset completedAt)
    {
        CompletedIssue completed = new(source.Id, completedAt);
        completed.CopySharedPropertiesFrom(source);
        completed.BranchName = source.BranchName;
        completed.PullRequestUrl = source.PullRequestUrl;
        completed.CompletedAt = completedAt;
        return completed;
    }

    internal static CompletedIssue FromChecksPending(ChecksPendingIssue source, DateTimeOffset completedAt)
    {
        CompletedIssue completed = new(source.Id, completedAt);
        completed.CopySharedPropertiesFrom(source);
        completed.BranchName = source.BranchName;
        completed.PullRequestUrl = source.PullRequestUrl;
        completed.CompletedAt = completedAt;
        return completed;
    }

    internal static CompletedIssue FromAwaitingInput(AwaitingInputIssue source, DateTimeOffset completedAt)
    {
        CompletedIssue completed = new(source.Id, completedAt);
        completed.CopySharedPropertiesFrom(source);
        (completed.BranchName, completed.PullRequestUrl) = source.ResumeTarget switch
        {
            ResumeTarget.Revision revision => (revision.BranchName, revision.PullRequestUrl),
            ResumeTarget.Continuation continuation => (continuation.BranchName, string.Empty),
            _ => (string.Empty, string.Empty),
        };
        completed.CompletedAt = completedAt;
        return completed;
    }

    internal static CompletedIssue FromPublishBlocked(PublishBlockedIssue source, DateTimeOffset completedAt)
    {
        CompletedIssue completed = new(source.Id, completedAt);
        completed.CopySharedPropertiesFrom(source);
        completed.BranchName = source.BranchName;
        completed.PullRequestUrl = source.PullRequestUrl;
        completed.CompletedAt = completedAt;
        return completed;
    }

    internal static CompletedIssue FromPublishFailed(PublishFailedIssue source, DateTimeOffset completedAt)
    {
        CompletedIssue completed = new(source.Id, completedAt);
        completed.CopySharedPropertiesFrom(source);
        completed.BranchName = source.BranchName;
        completed.PullRequestUrl = source.PullRequestUrl;
        completed.CompletedAt = completedAt;
        return completed;
    }

    internal static CompletedIssue FromInProgress(
        InProgressIssue source,
        string branchName,
        string pullRequestUrl,
        DateTimeOffset completedAt)
    {
        CompletedIssue completed = new(source.Id, completedAt);
        completed.CopySharedPropertiesFrom(source);
        completed.BranchName = branchName;
        completed.PullRequestUrl = pullRequestUrl;
        completed.CompletedAt = completedAt;
        return completed;
    }
}
