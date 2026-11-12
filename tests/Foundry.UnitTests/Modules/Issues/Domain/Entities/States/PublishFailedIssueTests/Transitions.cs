using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.PublishFailedIssueTests;

public sealed class Transitions
{
    private static readonly DateTimeOffset FailedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenImplementRunPublishFails_KeepsRunBranchAndReason()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();

        // Act
        PublishFailedIssue publishFailed = inProgress.MarkPublishFailed("feat/1-test", "push rejected", FailedAt);

        // Assert
        IssuePublishFailed domainEvent = inProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssuePublishFailed>();
        publishFailed.ShouldSatisfyAllConditions(
            () => publishFailed.Id.ShouldBe(inProgress.Id),
            () => publishFailed.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => publishFailed.BranchName.ShouldBe("feat/1-test"),
            () => publishFailed.FailureReason.ShouldBe("push rejected"),
            () => publishFailed.FailedAt.ShouldBe(FailedAt),
            () => publishFailed.IsRevision.ShouldBeFalse(),
            () => publishFailed.LinkedPullRequestUrl.ShouldBeNull(),
            () => domainEvent.IssueId.ShouldBe(inProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenRevisionRunPublishFails_KeepsTheRevisionContext()
    {
        // Arrange
        RevisionInProgressIssue revision = new IssueBuilder()
            .WithBranchName("feat/2-fix")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/2")
            .RevisionInProgress();

        // Act
        PublishFailedIssue publishFailed = revision.MarkPublishFailed("push rejected", FailedAt);

        // Assert
        revision.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssuePublishFailed>();
        publishFailed.ShouldSatisfyAllConditions(
            () => publishFailed.WorkerRunId.ShouldBe(revision.WorkerRunId),
            () => publishFailed.BranchName.ShouldBe("feat/2-fix"),
            () => publishFailed.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/2"),
            () => publishFailed.LinkedPullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/2"),
            () => publishFailed.IsRevision.ShouldBeTrue(),
            () => publishFailed.Trigger.ShouldBe(revision.Trigger),
            () => publishFailed.ChecksFixAttempts.ShouldBe(revision.ChecksFixAttempts),
            () => publishFailed.RebaseConflictAttempts.ShouldBe(revision.RebaseConflictAttempts));
    }

    [Fact]
    public void WhenImplementPublishIsRetried_ReturnsToInProgressWithTheSameRun()
    {
        // Arrange
        PublishFailedIssue publishFailed = new IssueBuilder().PublishFailed();

        // Act
        Issue next = publishFailed.RetryPublish();

        // Assert
        InProgressIssue inProgress = next.ShouldBeOfType<InProgressIssue>();
        publishFailed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInProgress>();
        inProgress.ShouldSatisfyAllConditions(
            () => inProgress.Id.ShouldBe(publishFailed.Id),
            () => inProgress.WorkerRunId.ShouldBe(publishFailed.WorkerRunId));
    }

    [Fact]
    public void WhenRevisionPublishIsRetried_ReturnsToRevisionInProgressWithItsContext()
    {
        // Arrange
        IssueBuilder builder = new IssueBuilder()
            .WithBranchName("feat/2-fix")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/2");
        PublishFailedIssue publishFailed = builder.PublishFailedFromRevision();

        // Act
        Issue next = publishFailed.RetryPublish();

        // Assert
        RevisionInProgressIssue revision = next.ShouldBeOfType<RevisionInProgressIssue>();
        publishFailed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionInProgress>();
        revision.ShouldSatisfyAllConditions(
            () => revision.WorkerRunId.ShouldBe(publishFailed.WorkerRunId),
            () => revision.BranchName.ShouldBe("feat/2-fix"),
            () => revision.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/2"),
            () => revision.Trigger.ShouldBe(publishFailed.Trigger));
    }

    [Fact]
    public void WhenImplementRunIsDiscarded_FailsContinuablyOnItsBranch()
    {
        // Arrange
        PublishFailedIssue publishFailed = new IssueBuilder().WithBranchName("feat/1-test").PublishFailed();

        // Act
        Issue next = publishFailed.Fail("discarded", FailureCategory.PublishExpired, FailedAt);

        // Assert
        ContinuableFailedIssue failed = next.ShouldBeOfType<ContinuableFailedIssue>();
        publishFailed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuableFailed>();
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(publishFailed.WorkerRunId),
            () => failed.BranchName.ShouldBe("feat/1-test"),
            () => failed.FailureReason.ShouldBe("discarded"),
            () => failed.FailureCategory.ShouldBe(FailureCategory.PublishExpired),
            () => failed.FailedAt.ShouldBe(FailedAt));
    }

    [Fact]
    public void WhenRevisionRunIsDiscarded_FailsAsARevision()
    {
        // Arrange
        PublishFailedIssue publishFailed = new IssueBuilder().PublishFailedFromRevision();

        // Act
        Issue next = publishFailed.Fail("discarded", FailureCategory.PublishExpired, FailedAt);

        // Assert
        RevisionFailedIssue failed = next.ShouldBeOfType<RevisionFailedIssue>();
        publishFailed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionFailed>();
        failed.ShouldSatisfyAllConditions(
            () => failed.BranchName.ShouldBe(publishFailed.BranchName),
            () => failed.PullRequestUrl.ShouldBe(publishFailed.PullRequestUrl),
            () => failed.Trigger.ShouldBe(publishFailed.Trigger),
            () => failed.FailureCategory.ShouldBe(FailureCategory.PublishExpired));
    }

    [Fact]
    public void PublishFailedIssue_IsNotARestingState()
    {
        // Arrange
        PublishFailedIssue publishFailed = new IssueBuilder().PublishFailed();

        // Act
        bool resting = publishFailed.IsRestingState();

        // Assert
        resting.ShouldBeFalse();
    }
}
