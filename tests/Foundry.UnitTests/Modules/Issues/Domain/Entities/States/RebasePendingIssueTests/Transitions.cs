using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RebasePendingIssueTests;

public sealed class Transitions
{
    [Fact]
    public void MarkChecksPending_CarriesRunBranchCutoffAndOutcome()
    {
        // Arrange
        DateTimeOffset feedbackCutoffAt = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        RebasePendingIssue rebasePending = new IssueBuilder()
            .WithFeedbackCutoffAt(feedbackCutoffAt)
            .RebasePending();
        RebaseOutcome outcome = new RebaseOutcome.Skipped(RebaseSkipReasons.Timeout);

        // Act
        ChecksPendingIssue checksPending = rebasePending.MarkChecksPending(outcome);

        // Assert
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.Id.ShouldBe(rebasePending.Id),
            () => checksPending.WorkerRunId.ShouldBe(rebasePending.WorkerRunId),
            () => checksPending.BranchName.ShouldBe(rebasePending.BranchName),
            () => checksPending.PullRequestUrl.ShouldBe(rebasePending.PullRequestUrl),
            () => checksPending.FeedbackCutoffAt.ShouldBe(feedbackCutoffAt),
            () => checksPending.ChecksFixAttempts.ShouldBe(rebasePending.ChecksFixAttempts),
            () => checksPending.RebaseOutcome.ShouldBe(outcome));
    }

    [Fact]
    public void MarkChecksPending_SetsEnteredChecksPendingAtToNow()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePending();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        ChecksPendingIssue checksPending = rebasePending.MarkChecksPending(new RebaseOutcome.Rebased());

        // Assert
        checksPending.EnteredChecksPendingAt.ShouldBeInRange(before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void MarkChecksPending_RaisesIssueChecksPending()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RebasePendingIssue rebasePending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RebasePending();

        // Act
        ChecksPendingIssue checksPending = rebasePending.MarkChecksPending(new RebaseOutcome.UpToDate());

        // Assert
        IssueChecksPending domainEvent = rebasePending.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueChecksPending>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(rebasePending.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => domainEvent.EnteredChecksPendingAt.ShouldBe(checksPending.EnteredChecksPendingAt));
    }

    [Fact]
    public void Complete_TransitionsToCompletedAndRaisesIssueCompleted()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePending();
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        CompletedIssue completed = rebasePending.Complete(completedAt);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.BranchName.ShouldBe(rebasePending.BranchName),
            () => completed.PullRequestUrl.ShouldBe(rebasePending.PullRequestUrl),
            () => completed.CompletedAt.ShouldBe(completedAt));
        rebasePending.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueCompleted>();
    }

    [Fact]
    public void Fail_TransitionsToContinuableFailedAndRaisesIssueContinuableFailed()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePending();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue failed = rebasePending.Fail("Pull request closed without merge", FailureCategory.PrClosed, failedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(rebasePending.WorkerRunId),
            () => failed.BranchName.ShouldBe(rebasePending.BranchName),
            () => failed.FailureCategory.ShouldBe(FailureCategory.PrClosed),
            () => failed.FailedAt.ShouldBe(failedAt));
        rebasePending.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuableFailed>();
    }

    [Fact]
    public void ChecksPendingToReview_CarriesRebaseOutcome()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithRebaseOutcome(new RebaseOutcome.Conflict())
            .ChecksPending();

        // Act
        ReviewIssue review = checksPending.ToReview(new ChecksOutcome.Passed());

        // Assert
        review.RebaseOutcome.ShouldBeOfType<RebaseOutcome.Conflict>();
    }
}
