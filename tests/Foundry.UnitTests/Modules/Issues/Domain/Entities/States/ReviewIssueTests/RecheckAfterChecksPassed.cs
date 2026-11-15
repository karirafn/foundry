using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewIssueTests;

public sealed class RecheckAfterChecksPassed
{
    private static readonly DateTimeOffset TransitionedAt = new(2026, 11, 15, 9, 30, 0, TimeSpan.Zero);

    public static TheoryData<ChecksOutcome> NonPassedOutcomes() =>
    [
        new ChecksOutcome.StillFailing([new FailingCheck("build", "failed", "https://ci.example/1")]),
        new ChecksOutcome.Unfinished(),
        new ChecksOutcome.Unreadable(),
        new ChecksOutcome.NoneDetected(),
    ];

    [Theory]
    [MemberData(nameof(NonPassedOutcomes))]
    public void WhenOutcomeIsNotPassed_ReturnsChecksPendingIssueWithChecksRerunReason(ChecksOutcome outcome)
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().WithChecksOutcome(outcome).Review();

        // Act
        ChecksPendingIssue? checksPending = review.RecheckAfterChecksPassed(TransitionedAt);

        // Assert
        checksPending.ShouldNotBeNull().Reason.ShouldBe(new ChecksPendingReason.ChecksRerun());
    }

    [Fact]
    public void WhenOutcomeIsNotPassed_StartsWithExhaustedFixBudget()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().WithChecksOutcome(new ChecksOutcome.Unreadable()).Review();

        // Act
        ChecksPendingIssue? checksPending = review.RecheckAfterChecksPassed(TransitionedAt);

        // Assert
        checksPending.ShouldNotBeNull().ChecksFixAttempts.ShouldBe(ChecksPendingIssue.ChecksMaxFixPushes);
    }

    [Fact]
    public void WhenOutcomeIsNotPassed_CarriesPullRequestFieldsAndEntersAtTransitionTime()
    {
        // Arrange
        DateTimeOffset feedbackCutoffAt = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        ReviewIssue review = new IssueBuilder()
            .WithBranchName("feat/7-rerun")
            .WithPullRequestUrl("https://gitlab.com/group/project/-/merge_requests/7")
            .WithFeedbackCutoffAt(feedbackCutoffAt)
            .WithRebaseOutcome(new RebaseOutcome.Rebased())
            .WithChecksOutcome(new ChecksOutcome.StillFailing([]))
            .Review();

        // Act
        ChecksPendingIssue checksPending = review.RecheckAfterChecksPassed(TransitionedAt).ShouldNotBeNull();

        // Assert
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.Id.ShouldBe(review.Id),
            () => checksPending.EnteredChecksPendingAt.ShouldBe(TransitionedAt),
            () => checksPending.EnteredStateAt.ShouldBe(TransitionedAt),
            () => checksPending.WorkerRunId.ShouldBe(review.WorkerRunId),
            () => checksPending.BranchName.ShouldBe("feat/7-rerun"),
            () => checksPending.PullRequestUrl.ShouldBe("https://gitlab.com/group/project/-/merge_requests/7"),
            () => checksPending.FeedbackCutoffAt.ShouldBe(review.FeedbackCutoffAt),
            () => checksPending.RebaseOutcome.ShouldBe(new RebaseOutcome.Rebased()),
            () => checksPending.RebaseConflictAttempts.ShouldBe(review.RebaseConflictAttempts));
    }

    [Fact]
    public void WhenOutcomeIsNotPassed_RaisesIssueChecksPendingDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(42)
            .WithChecksOutcome(new ChecksOutcome.Unfinished())
            .Review();

        // Act
        review.RecheckAfterChecksPassed(TransitionedAt);

        // Assert
        IssueChecksPending domainEvent = review.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueChecksPending>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(review.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => domainEvent.IssueNumber.ShouldBe(42),
            () => domainEvent.EnteredChecksPendingAt.ShouldBe(TransitionedAt));
    }

    [Fact]
    public void WhenOutcomeIsPassed_RefusesAndRaisesNoDomainEvent()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().WithChecksOutcome(new ChecksOutcome.Passed()).Review();

        // Act
        ChecksPendingIssue? checksPending = review.RecheckAfterChecksPassed(TransitionedAt);

        // Assert
        review.ShouldSatisfyAllConditions(
            () => checksPending.ShouldBeNull(),
            () => review.DomainEvents.ShouldBeEmpty());
    }

    [Fact]
    public void WhenGateReadsGreenAndAutoMergeIsOn_RoutesToMergePending()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().WithChecksOutcome(new ChecksOutcome.StillFailing([])).Review();
        ChecksPendingIssue checksPending = review.RecheckAfterChecksPassed(TransitionedAt).ShouldNotBeNull();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, "0a1b2c3d", TransitionedAt);

        // Assert
        next.ShouldBeOfType<MergePendingIssue>();
    }

    [Fact]
    public void WhenGateReadsGreenAndAutoMergeIsOff_RoutesToReviewWithPassed()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().WithChecksOutcome(new ChecksOutcome.StillFailing([])).Review();
        ChecksPendingIssue checksPending = review.RecheckAfterChecksPassed(TransitionedAt).ShouldNotBeNull();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, "0a1b2c3d", TransitionedAt);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome.ShouldBe(new ChecksOutcome.Passed());
    }

    [Fact]
    public void WhenGateReadsRed_RoutesToReviewStillFailingWithoutQueueingAFix()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().WithChecksOutcome(new ChecksOutcome.Unreadable()).Review();
        ChecksPendingIssue checksPending = review.RecheckAfterChecksPassed(TransitionedAt).ShouldNotBeNull();
        IReadOnlyList<FailingCheck> failing = [new FailingCheck("test", "failed", "https://ci.example/2")];

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.StillFailing(failing), autoMerge: true, "0a1b2c3d", TransitionedAt);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome.ShouldBeOfType<ChecksOutcome.StillFailing>();
    }
}
