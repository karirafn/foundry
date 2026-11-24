using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RevisionBlockedIssueTests;

public sealed class Unblock
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenUnblocked_RequeuesTheSameRevisionOnTheSameBranchAndPullRequest()
    {
        // Arrange
        RevisionQueuedIssue queued = new IssueBuilder()
            .WithBranchName("feat/42-x")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/42")
            .WithNewestCommentAt(Now.AddHours(-1))
            .RevisionQueued();
        RevisionBlockedIssue blocked = queued.Block([5], Now);

        // Act
        RevisionQueuedIssue requeued = blocked.Unblock(Now);

        // Assert
        requeued.ShouldSatisfyAllConditions(
            () => requeued.Id.ShouldBe(queued.Id),
            () => requeued.BranchName.ShouldBe(queued.BranchName),
            () => requeued.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/42"),
            () => requeued.Trigger.ShouldBe(queued.Trigger),
            () => requeued.PriorFeedbackCutoffAt.ShouldBe(queued.PriorFeedbackCutoffAt),
            () => requeued.RebaseConflictAttempts.ShouldBe(queued.RebaseConflictAttempts),
            () => requeued.ChecksFixAttempts.ShouldBe(queued.ChecksFixAttempts),
            () => requeued.Round.ShouldBe(queued.Round),
            () => requeued.HasPersonFeedback.ShouldBe(queued.HasPersonFeedback));
    }

    [Fact]
    public void WhenAReviewFindingsRevisionIsUnblocked_KeepsTheFeedbackCutoffItStartedFrom()
    {
        // Arrange
        RevisionQueuedIssue queued = new IssueBuilder().RevisionQueuedFromReviewFindings();
        RevisionBlockedIssue blocked = queued.Block([5], Now);

        // Act
        RevisionQueuedIssue requeued = blocked.Unblock(Now);

        // Assert
        requeued.ShouldSatisfyAllConditions(
            () => requeued.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFindings>(),
            () => requeued.PriorFeedbackCutoffAt.ShouldNotBeNull(),
            () => requeued.PriorFeedbackCutoffAt.ShouldBe(queued.PriorFeedbackCutoffAt),
            () => requeued.ReviewFixAttempts.ShouldBe(queued.ReviewFixAttempts));
    }

    [Fact]
    public void WhenARevisionContinuedWithoutAnswerIsUnblocked_StillRefusesQuestions()
    {
        // Arrange
        RevisionQueuedIssue queued = (RevisionQueuedIssue)new IssueBuilder()
            .AwaitingInputPosted(Now.AddMinutes(-5))
            .ContinueWithoutAnswer(Now);
        RevisionBlockedIssue blocked = queued.Block([5], Now);

        // Act
        RevisionQueuedIssue requeued = blocked.Unblock(Now);

        // Assert
        requeued.ContinuedWithoutAnswer.ShouldBeTrue();
    }

    [Fact]
    public void WhenUnblocked_RaisesIssueRevisionQueued()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionBlockedIssue blocked = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).RevisionBlocked([5]);

        // Act
        blocked.Unblock(Now);

        // Assert
        IssueRevisionQueued domainEvent = blocked.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(blocked.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
