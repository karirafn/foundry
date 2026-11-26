using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

/// <summary>
/// The issue records the pull request Foundry marked ready for review: the flag holds only for that pull request,
/// survives every transition, and a new pull request starts unmarked.
/// </summary>
public sealed class IsPullRequestMarkedReady
{
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/1";
    private const string NewPullRequestUrl = "https://github.com/owner/repo/pull/2";

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static IssueBuilder Builder() => new IssueBuilder().WithPullRequestUrl(PullRequestUrl);

    [Fact]
    public void WhenNeverMarked_IsFalse()
    {
        // Act
        ReviewIssue review = Builder().Review();

        // Assert
        review.IsPullRequestMarkedReady.ShouldBeFalse();
        review.MarkedReadyPullRequestUrl.ShouldBeNull();
    }

    [Fact]
    public void WhenIssueRecordsItsPullRequest_IsTrue()
    {
        // Arrange
        ReviewIssue review = Builder().Review();

        // Act
        bool recorded = review.RecordPullRequestMarkedReady(PullRequestUrl);

        // Assert
        recorded.ShouldBeTrue();
        review.IsPullRequestMarkedReady.ShouldBeTrue();
        review.MarkedReadyPullRequestUrl.ShouldBe(PullRequestUrl);
    }

    [Fact]
    public void WhenAlreadyRecorded_RecordingAgainChangesNothing()
    {
        // Arrange
        ReviewIssue review = Builder().Review();
        review.RecordPullRequestMarkedReady(PullRequestUrl);

        // Act
        bool recorded = review.RecordPullRequestMarkedReady(PullRequestUrl);

        // Assert
        recorded.ShouldBeFalse();
        review.IsPullRequestMarkedReady.ShouldBeTrue();
    }

    [Fact]
    public void WhenUrlIsNotTheIssuesPullRequest_RecordsNothing()
    {
        // Arrange
        ReviewIssue review = Builder().Review();
        review.RecordPullRequestMarkedReady(PullRequestUrl);

        // Act
        bool recorded = review.RecordPullRequestMarkedReady(NewPullRequestUrl);

        // Assert
        recorded.ShouldBeFalse();
        review.MarkedReadyPullRequestUrl.ShouldBe(PullRequestUrl);
        review.IsPullRequestMarkedReady.ShouldBeTrue();
    }

    [Fact]
    public void WhenIssueHoldsNoPullRequest_RecordsNothing()
    {
        // Arrange
        InProgressIssue inProgress = Builder().InProgress();

        // Act
        bool recorded = inProgress.RecordPullRequestMarkedReady(PullRequestUrl);

        // Assert
        recorded.ShouldBeFalse();
        inProgress.MarkedReadyPullRequestUrl.ShouldBeNull();
        inProgress.IsPullRequestMarkedReady.ShouldBeFalse();
    }

    [Fact]
    public void WhenReviewInProgressIssueMovesToReview_StaysMarked()
    {
        // Arrange
        ReviewInProgressIssue reviewInProgress = Builder().ReviewInProgress();
        reviewInProgress.RecordPullRequestMarkedReady(PullRequestUrl);

        // Act
        RebasePendingIssue beforeReady = (RebasePendingIssue)reviewInProgress.MarkRunFailed(
            "Review verdict invalid",
            FailureCategory.VerdictInvalid,
            consecutiveAutoRetryableFailures: 0,
            Now);
        Issue next = beforeReady.ResolveRebase(new RebaseOutcome.UpToDate(), Now);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.IsPullRequestMarkedReady.ShouldBeTrue();
        review.MarkedReadyPullRequestUrl.ShouldBe(PullRequestUrl);
    }

    [Fact]
    public void WhenFreshRunOpensNewPullRequest_IsFalse()
    {
        // Arrange
        ReviewIssue review = Builder().Review();
        review.RecordPullRequestMarkedReady(PullRequestUrl);
        FreshQueuedIssue requeued = review
            .Fail("Pull request closed without merge", FailureCategory.PrClosed, Now)
            .RetryFresh(Now);

        // Act
        RebasePendingIssue rebasePending = requeued
            .Claim(WorkerRunId.New(), Now)
            .MarkRebasePending("feat/1-test", NewPullRequestUrl, Now, Now);

        // Assert
        rebasePending.MarkedReadyPullRequestUrl.ShouldBe(PullRequestUrl);
        rebasePending.IsPullRequestMarkedReady.ShouldBeFalse();
    }
}
