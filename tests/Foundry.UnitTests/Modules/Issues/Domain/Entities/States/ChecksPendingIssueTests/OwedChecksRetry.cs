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

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ChecksPendingIssueTests;

/// <summary>
/// A failed-checks fix run that pushes nothing owes the red head one retry of its failing CI jobs before a red
/// verdict on that head goes to review.
/// </summary>
public sealed class OwedChecksRetry
{
    private const string RedHead = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";
    private const string MovedHead = "9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b";

    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<FailingCheck> Failing =
    [
        new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1/job/2"),
    ];

    [Fact]
    public void WhenRedVerdictQueuesAFix_TheFixRunCarriesTheRedHead()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.StillFailing(Failing), false, false, RedHead, Now);

        // Assert
        next.ShouldBeOfType<RevisionQueuedIssue>().Trigger
            .ShouldBeOfType<RevisionTrigger.FailedChecks>().HeadSha.ShouldBe(RedHead);
    }

    [Fact]
    public void WhenFixRunPushesNothing_OwesARetryForTheRedHead()
    {
        // Arrange
        RevisionInProgressIssue fixRun = FixRun();

        // Act
        RebasePendingIssue rebasePending = fixRun.MarkUnchangedRebasePending(Now, Now);

        // Assert
        rebasePending.OwedChecksRetryHeadSha.ShouldBe(RedHead);
    }

    [Fact]
    public void WhenFixRunPushes_OwesNoRetry()
    {
        // Arrange
        RevisionInProgressIssue fixRun = FixRun();

        // Act
        RebasePendingIssue rebasePending = fixRun.MarkRebasePending(Now, Now);

        // Assert
        rebasePending.OwedChecksRetryHeadSha.ShouldBeNull();
    }

    [Fact]
    public void WhenUnchangedRebaseLeavesTheHead_TheOwedRetryReachesTheChecksGate()
    {
        // Arrange
        RebasePendingIssue rebasePending = FixRun().MarkUnchangedRebasePending(Now, Now);

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.UpToDate(), Now);

        // Assert
        next.ShouldBeOfType<ChecksPendingIssue>().OwedChecksRetryHeadSha.ShouldBe(RedHead);
    }

    [Fact]
    public void WhenTheRebaseMovesTheHead_TheOwedRetryIsCleared()
    {
        // Arrange
        RebasePendingIssue rebasePending = FixRun().MarkUnchangedRebasePending(Now, Now);

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.Rebased(), Now);

        // Assert
        next.ShouldBeOfType<ChecksPendingIssue>().OwedChecksRetryHeadSha.ShouldBeNull();
    }

    [Fact]
    public void OwesChecksRetryFor_IsTrueOnlyForTheRedHead()
    {
        // Arrange
        ChecksPendingIssue checksPending = OwingRetry();

        // Act
        bool onRedHead = checksPending.OwesChecksRetryFor(RedHead);
        bool onMovedHead = checksPending.OwesChecksRetryFor(MovedHead);
        bool onUnknownHead = checksPending.OwesChecksRetryFor(null);

        // Assert
        checksPending.ShouldSatisfyAllConditions(
            () => onRedHead.ShouldBeTrue(),
            () => onMovedHead.ShouldBeFalse(),
            () => onUnknownHead.ShouldBeFalse());
    }

    [Fact]
    public void RecordChecksRetried_StaysInTheGateWithTheRetrySpentAndTheClockRestarted()
    {
        // Arrange
        ChecksPendingIssue checksPending = OwingRetry();
        DateTimeOffset retriedAt = Now.AddMinutes(30);

        // Act
        ChecksPendingIssue retried = checksPending.RecordChecksRetried(RedHead, retriedAt);

        // Assert
        retried.ShouldSatisfyAllConditions(
            () => retried.OwedChecksRetryHeadSha.ShouldBeNull(),
            () => retried.ChecksRetriedAt.ShouldBe(retriedAt),
            () => retried.EnteredChecksPendingAt.ShouldBe(retriedAt),
            () => retried.PullRequestUrl.ShouldBe(checksPending.PullRequestUrl),
            () => retried.ChecksFixAttempts.ShouldBe(ChecksPendingIssue.ChecksMaxFixPushes));
    }

    [Fact]
    public void RecordChecksRetried_RaisesChecksPendingAtTheRestartedClock()
    {
        // Arrange
        ChecksPendingIssue checksPending = OwingRetry();
        DateTimeOffset retriedAt = Now.AddMinutes(30);

        // Act
        checksPending.RecordChecksRetried(RedHead, retriedAt);

        // Assert
        checksPending.DomainEvents.OfType<IssueChecksPending>().Single().EnteredChecksPendingAt.ShouldBe(retriedAt);
    }

    [Fact]
    public void RecordChecksRetried_OnAnotherHead_Throws()
    {
        // Arrange
        ChecksPendingIssue checksPending = OwingRetry();

        // Act
        Action act = () => checksPending.RecordChecksRetried(MovedHead, Now);

        // Assert
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void WhenRetriedVerdictStillFails_GoesToReviewWithoutAFix()
    {
        // Arrange
        ChecksPendingIssue retried = OwingRetry().RecordChecksRetried(RedHead, Now);
        retried.RecordChecksRestarted();
        ChecksOutcome.StillFailing stillFailing = new(Failing);

        // Act
        Issue next = retried.ResolveVerdict(stillFailing, autoMerge: true, reviewPullRequests: false, RedHead, Now);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome.ShouldBe(stillFailing);
    }

    [Fact]
    public void WhenRetriedVerdictPassesWithAutoMerge_GoesToMergePending()
    {
        // Arrange
        ChecksPendingIssue retried = OwingRetry().RecordChecksRetried(RedHead, Now);

        // Act
        Issue next = retried.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, reviewPullRequests: false, RedHead, Now);

        // Assert
        next.ShouldBeOfType<MergePendingIssue>();
    }

    [Fact]
    public void WhenRetriedVerdictPassesWithReview_QueuesTheReview()
    {
        // Arrange
        ChecksPendingIssue retried = OwingRetry().RecordChecksRetried(RedHead, Now);

        // Act
        Issue next = retried.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, reviewPullRequests: true, RedHead, Now);

        // Assert
        next.ShouldBeOfType<ReviewQueuedIssue>();
    }

    [Fact]
    public void WhenNoRetryIsPossible_TheRedVerdictGoesToReview()
    {
        // Arrange
        ChecksPendingIssue checksPending = OwingRetry();
        ChecksOutcome.StillFailing stillFailing = new(Failing);

        // Act
        Issue next = checksPending.ResolveVerdict(stillFailing, autoMerge: true, reviewPullRequests: false, RedHead, Now);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome.ShouldBe(stillFailing);
    }

    [Fact]
    public void RecordChecksRestarted_ClearsTheWaitForTheRetriedJobs()
    {
        // Arrange
        ChecksPendingIssue retried = OwingRetry().RecordChecksRetried(RedHead, Now);

        // Act
        bool changed = retried.RecordChecksRestarted();

        // Assert
        retried.ShouldSatisfyAllConditions(
            () => changed.ShouldBeTrue(),
            () => retried.ChecksRetriedAt.ShouldBeNull());
    }

    [Fact]
    public void RecordChecksRestarted_WithoutARetry_ChangesNothing()
    {
        // Arrange
        ChecksPendingIssue checksPending = OwingRetry();

        // Act
        bool changed = checksPending.RecordChecksRestarted();

        // Assert
        changed.ShouldBeFalse();
    }

    private static RevisionInProgressIssue FixRun() =>
        new IssueBuilder()
            .ChecksPending()
            .FixChecks(Failing, Now, RedHead)
            .Claim(WorkerRunId.New(), Now);

    private static ChecksPendingIssue OwingRetry() =>
        FixRun()
            .MarkUnchangedRebasePending(Now, Now)
            .MarkChecksPending(new RebaseOutcome.UpToDate(), Now);
}
