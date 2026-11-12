using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.BranchDivergedIssueTests;

public sealed class Transitions
{
    [Fact]
    public void WhenImplementRunDiverges_KeepsBothBranchesAndThePullRequest()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();

        // Act
        BranchDivergedIssue diverged = inProgress.MarkBranchDiverged(
            "feat/1-test",
            "feat/1-test-run-abc",
            "https://github.com/owner/repo/pull/1");

        // Assert
        IssueBranchDiverged domainEvent = inProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueBranchDiverged>();
        diverged.ShouldSatisfyAllConditions(
            () => diverged.Id.ShouldBe(inProgress.Id),
            () => diverged.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => diverged.BranchName.ShouldBe("feat/1-test"),
            () => diverged.SideBranchName.ShouldBe("feat/1-test-run-abc"),
            () => diverged.LinkedPullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/1"),
            () => diverged.FromRevision.ShouldBeFalse(),
            () => domainEvent.IssueId.ShouldBe(inProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenImplementRunDivergesWithoutPullRequest_HasNoLinkedPullRequest()
    {
        // Arrange
        InProgressIssue inProgress = new IssueBuilder().InProgress();

        // Act
        BranchDivergedIssue diverged = inProgress.MarkBranchDiverged("feat/1-test", "feat/1-test-run-abc", null);

        // Assert
        diverged.LinkedPullRequestUrl.ShouldBeNull();
    }

    [Fact]
    public void WhenRevisionRunDiverges_RemembersItCameFromARevision()
    {
        // Arrange
        RevisionInProgressIssue revision = new IssueBuilder()
            .WithBranchName("feat/2-fix")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/2")
            .RevisionInProgress();

        // Act
        BranchDivergedIssue diverged = revision.MarkBranchDiverged("feat/2-fix-run-abc");

        // Assert
        revision.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueBranchDiverged>();
        diverged.ShouldSatisfyAllConditions(
            () => diverged.WorkerRunId.ShouldBe(revision.WorkerRunId),
            () => diverged.BranchName.ShouldBe("feat/2-fix"),
            () => diverged.SideBranchName.ShouldBe("feat/2-fix-run-abc"),
            () => diverged.LinkedPullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/2"),
            () => diverged.FromRevision.ShouldBeTrue());
    }

    [Fact]
    public void WhenRetried_QueuesAContinuationOnTheIssueBranch()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        BranchDivergedIssue diverged = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithBranchName("feat/1-test")
            .BranchDiverged("feat/1-test-run-abc");

        // Act
        ContinuationQueuedIssue queued = diverged.Retry();

        // Assert
        IssueContinuationQueued domainEvent = diverged.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuationQueued>();
        DispatchContext.Continuation context = queued.Context.ShouldBeOfType<DispatchContext.Continuation>();
        queued.ShouldSatisfyAllConditions(
            () => queued.Id.ShouldBe(diverged.Id),
            () => queued.BranchName.ShouldBe("feat/1-test"),
            () => context.BranchName.ShouldBe("feat/1-test"),
            () => context.FailureReason.ShouldNotBeNull().ShouldContain("feat/1-test-run-abc"),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void BranchDivergedIssue_IsARestingState()
    {
        // Arrange
        BranchDivergedIssue diverged = new IssueBuilder().BranchDiverged();

        // Act
        bool resting = diverged.IsRestingState();

        // Assert
        resting.ShouldBeTrue();
    }
}
