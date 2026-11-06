using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RebasePendingIssueTests;

public sealed class FromRevisionInProgress
{
    private static readonly DateTimeOffset RunStartedAt = new(2026, 6, 1, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenCreatedFromRevisionInProgress_CopiesSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();

        // Act
        RebasePendingIssue rebasePending = revisionInProgress.MarkRebasePending(RunStartedAt);

        // Assert
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Id.ShouldBe(revisionInProgress.Id),
            () => rebasePending.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => rebasePending.IssueNumber.ShouldBe(revisionInProgress.IssueNumber),
            () => rebasePending.Title.ShouldBe(revisionInProgress.Title),
            () => rebasePending.Author.ShouldBe(revisionInProgress.Author),
            () => rebasePending.Url.ShouldBe(revisionInProgress.Url),
            () => rebasePending.DetectedAt.ShouldBe(revisionInProgress.DetectedAt));
    }

    [Fact]
    public void WhenCreatedFromRevisionInProgress_CarriesWorkerRunId_BranchName_PullRequestUrl()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();

        // Act
        RebasePendingIssue rebasePending = revisionInProgress.MarkRebasePending(RunStartedAt);

        // Assert
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.WorkerRunId.ShouldBe(revisionInProgress.WorkerRunId),
            () => rebasePending.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => rebasePending.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl));
    }

    [Fact]
    public void WhenCreatedFromRevisionInProgress_WithNewestConsumedComment_UsesThatTimestampAsFeedbackCutoff()
    {
        // Arrange
        DateTimeOffset newestConsumedCommentAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithNewestCommentAt(newestConsumedCommentAt)
            .RevisionInProgress();

        // Act
        RebasePendingIssue rebasePending = revisionInProgress.MarkRebasePending(RunStartedAt);

        // Assert
        rebasePending.FeedbackCutoffAt.ShouldBe(newestConsumedCommentAt);
    }

    [Fact]
    public void WhenCreatedFromRevisionInProgress_WithNullNewestConsumedComment_FallsBackToRunStartedAt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();

        // Act
        RebasePendingIssue rebasePending = revisionInProgress.MarkRebasePending(RunStartedAt);

        // Assert
        rebasePending.FeedbackCutoffAt.ShouldBe(RunStartedAt);
    }

    [Fact]
    public void WhenCreatedFromRevisionInProgress_SetsEnteredRebasePendingAt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        RebasePendingIssue rebasePending = revisionInProgress.MarkRebasePending(RunStartedAt);

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        rebasePending.EnteredRebasePendingAt.ShouldBeInRange(before, after);
    }

    [Fact]
    public void WhenCreatedFromRevisionInProgress_RaisesIssueRebasePendingDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();

        // Act
        revisionInProgress.MarkRebasePending(RunStartedAt);

        // Assert
        IssueRebasePending domainEvent = revisionInProgress.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueRebasePending>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(revisionInProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenCreatedFromRevisionInProgress_ViaMarkUnchangedRebasePending_CopiesSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();

        // Act
        RebasePendingIssue rebasePending = revisionInProgress.MarkUnchangedRebasePending(RunStartedAt);

        // Assert
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Id.ShouldBe(revisionInProgress.Id),
            () => rebasePending.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => rebasePending.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => rebasePending.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl));
    }

    [Fact]
    public void WhenCreatedFromRevisionInProgress_ViaMarkUnchangedRebasePending_RaisesIssueRebasePendingDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();

        // Act
        revisionInProgress.MarkUnchangedRebasePending(RunStartedAt);

        // Assert
        IssueRebasePending domainEvent = revisionInProgress.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueRebasePending>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(revisionInProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
