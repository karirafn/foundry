using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RebasePendingIssueTests;

public sealed class ResolveRebase
{
    [Fact]
    public void WhenConflictBelowCap_QueuesRebaseConflictRevisionAndIncrementsCounter()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder()
            .WithBranchName("feat/1-thing")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/7")
            .RebasePending();

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.Conflict(), DateTimeOffset.UtcNow);

        // Assert
        RevisionQueuedIssue revisionQueued = next.ShouldBeOfType<RevisionQueuedIssue>();
        revisionQueued.ShouldSatisfyAllConditions(
            () => revisionQueued.Id.ShouldBe(rebasePending.Id),
            () => revisionQueued.Trigger.ShouldBeOfType<RevisionTrigger.RebaseConflict>(),
            () => revisionQueued.RebaseConflictAttempts.ShouldBe(1),
            () => revisionQueued.ChecksFixAttempts.ShouldBe(rebasePending.ChecksFixAttempts),
            () => revisionQueued.BranchName.ShouldBe("feat/1-thing"),
            () => revisionQueued.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/7"));
    }

    [Fact]
    public void WhenConflictBelowCap_RaisesIssueRevisionQueued()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RebasePendingIssue rebasePending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RebasePending();

        // Act
        rebasePending.ResolveRebase(new RebaseOutcome.Conflict(), DateTimeOffset.UtcNow);

        // Assert
        IssueRevisionQueued domainEvent = rebasePending.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueRevisionQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(rebasePending.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenSecondConsecutiveConflict_IncrementsCounterToCap()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePendingAfterConflicts(1);

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.Conflict(), DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<RevisionQueuedIssue>()
            .RebaseConflictAttempts.ShouldBe(RebasePendingIssue.MaxRebaseConflictAttempts);
    }

    [Fact]
    public void WhenConflictAtCap_FailsOpenToChecksPendingWithConflictOutcome()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder()
            .RebasePendingAfterConflicts(RebasePendingIssue.MaxRebaseConflictAttempts);

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.Conflict(), DateTimeOffset.UtcNow);

        // Assert
        ChecksPendingIssue checksPending = next.ShouldBeOfType<ChecksPendingIssue>();
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.RebaseOutcome.ShouldBeOfType<RebaseOutcome.Conflict>(),
            () => checksPending.RebaseConflictAttempts.ShouldBe(RebasePendingIssue.MaxRebaseConflictAttempts));
        rebasePending.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueChecksPending>();
    }

    [Fact]
    public void WhenResolveConflictCalledAtCap_Throws()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder()
            .RebasePendingAfterConflicts(RebasePendingIssue.MaxRebaseConflictAttempts);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => rebasePending.ResolveConflict(DateTimeOffset.UtcNow));
    }

    public static TheoryData<RebaseOutcome> SuccessfulSyncs => [new RebaseOutcome.UpToDate(), new RebaseOutcome.Rebased()];

    [Theory]
    [MemberData(nameof(SuccessfulSyncs))]
    public void WhenSuccessfulSync_ResetsCounter(RebaseOutcome outcome)
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePendingAfterConflicts(1);

        // Act
        Issue next = rebasePending.ResolveRebase(outcome, DateTimeOffset.UtcNow);

        // Assert
        ChecksPendingIssue checksPending = next.ShouldBeOfType<ChecksPendingIssue>();
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.RebaseOutcome.ShouldBe(outcome),
            () => checksPending.RebaseConflictAttempts.ShouldBe(0));
    }

    public static TheoryData<RebaseOutcome> NeutralOutcomes =>
        [new RebaseOutcome.Disabled(), new RebaseOutcome.Skipped(RebaseSkipReasons.Timeout)];

    [Theory]
    [MemberData(nameof(NeutralOutcomes))]
    public void WhenDisabledOrSkipped_LeavesCounterUnchanged(RebaseOutcome outcome)
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePendingAfterConflicts(1);

        // Act
        Issue next = rebasePending.ResolveRebase(outcome, DateTimeOffset.UtcNow);

        // Assert
        ChecksPendingIssue checksPending = next.ShouldBeOfType<ChecksPendingIssue>();
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.RebaseOutcome.ShouldBe(outcome),
            () => checksPending.RebaseConflictAttempts.ShouldBe(1));
    }

    [Fact]
    public void WhenConflictRevisionCompletes_CounterIsThreadedThroughEveryState()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePending();

        // Act
        RevisionQueuedIssue revisionQueued = rebasePending.ResolveRebase(new RebaseOutcome.Conflict(), DateTimeOffset.UtcNow)
            .ShouldBeOfType<RevisionQueuedIssue>();
        RevisionInProgressIssue revisionInProgress = revisionQueued.Claim(workerRunId, DateTimeOffset.UtcNow);
        RebasePendingIssue rebasePendingAgain = revisionInProgress.MarkUnchangedRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        ChecksPendingIssue checksPending = rebasePendingAgain.ResolveRebase(new RebaseOutcome.Disabled(), DateTimeOffset.UtcNow)
            .ShouldBeOfType<ChecksPendingIssue>();
        ReviewIssue review = checksPending.ToReview(new ChecksOutcome.Passed(), DateTimeOffset.UtcNow);

        // Assert
        new[]
        {
            revisionQueued.RebaseConflictAttempts,
            revisionInProgress.RebaseConflictAttempts,
            rebasePendingAgain.RebaseConflictAttempts,
            checksPending.RebaseConflictAttempts,
            review.RebaseConflictAttempts,
        }.ShouldAllBe(attempts => attempts == 1);
    }

    [Fact]
    public void WhenConflictRevisionFailsAndIsRetried_CounterIsPreserved()
    {
        // Arrange
        RevisionInProgressIssue revisionInProgress = new IssueBuilder().RebasePending()
            .ResolveConflict(DateTimeOffset.UtcNow)
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);

        // Act
        RevisionFailedIssue failed = revisionInProgress.MarkFailed(
            "boom",
            FailureCategory.NonZeroExit,
            DateTimeOffset.UtcNow);
        RevisionQueuedIssue retried = failed.Retry(DateTimeOffset.UtcNow);

        // Assert
        failed.RebaseConflictAttempts.ShouldBe(1);
        retried.ShouldSatisfyAllConditions(
            () => retried.RebaseConflictAttempts.ShouldBe(1),
            () => retried.Trigger.ShouldBeOfType<RevisionTrigger.RebaseConflict>());
    }

    [Fact]
    public void WhenFailedChecksOrReviewRevisionFollows_CounterIsUnchanged()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .RebasePendingAfterConflicts(1)
            .ResolveRebase(new RebaseOutcome.Skipped(RebaseSkipReasons.Timeout), DateTimeOffset.UtcNow)
            .ShouldBeOfType<ChecksPendingIssue>();

        // Act
        RevisionQueuedIssue fixChecks = checksPending.FixChecks([new FailingCheck("ci/build", "failure", null)], DateTimeOffset.UtcNow);
        RevisionQueuedIssue revise = checksPending.Revise([new ReviewComment("Fix it.")], DateTimeOffset.UtcNow);
        RevisionQueuedIssue fromReview = checksPending.ToReview(new ChecksOutcome.Passed(), DateTimeOffset.UtcNow)
            .Revise([new ReviewComment("Fix it.")], DateTimeOffset.UtcNow);

        // Assert
        new[]
        {
            fixChecks.RebaseConflictAttempts,
            revise.RebaseConflictAttempts,
            fromReview.RebaseConflictAttempts,
        }.ShouldAllBe(attempts => attempts == 1);
    }

    [Fact]
    public void WhenEnteredFromFreshRun_CounterStartsAtZero()
    {
        // Act
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePending();

        // Assert
        rebasePending.RebaseConflictAttempts.ShouldBe(0);
    }
}
