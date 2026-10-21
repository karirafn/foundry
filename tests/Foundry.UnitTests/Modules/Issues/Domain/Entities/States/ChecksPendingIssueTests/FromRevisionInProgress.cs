using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ChecksPendingIssueTests;

public sealed class FromRevisionInProgress
{
    [Fact]
    public void WhenCreatedFromRevisionInProgress_CopiesSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();

        // Act
        ChecksPendingIssue checksPending = revisionInProgress.MarkChecksPending();

        // Assert
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.Id.ShouldBe(revisionInProgress.Id),
            () => checksPending.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => checksPending.IssueNumber.ShouldBe(revisionInProgress.IssueNumber),
            () => checksPending.Title.ShouldBe(revisionInProgress.Title),
            () => checksPending.Author.ShouldBe(revisionInProgress.Author),
            () => checksPending.Url.ShouldBe(revisionInProgress.Url),
            () => checksPending.DetectedAt.ShouldBe(revisionInProgress.DetectedAt));
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
        ChecksPendingIssue checksPending = revisionInProgress.MarkChecksPending();

        // Assert
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.WorkerRunId.ShouldBe(revisionInProgress.WorkerRunId),
            () => checksPending.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => checksPending.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl));
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
        ChecksPendingIssue checksPending = revisionInProgress.MarkChecksPending();

        // Assert
        checksPending.FeedbackCutoffAt.ShouldBe(newestConsumedCommentAt);
    }

    [Fact]
    public void WhenCreatedFromRevisionInProgress_WithNullNewestConsumedComment_UsesFallbackUtcNow()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        ChecksPendingIssue checksPending = revisionInProgress.MarkChecksPending();

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        checksPending.FeedbackCutoffAt.ShouldBeInRange(before, after);
    }

    [Fact]
    public void WhenCreatedFromRevisionInProgress_SetsEnteredChecksPendingAt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        ChecksPendingIssue checksPending = revisionInProgress.MarkChecksPending();

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        checksPending.EnteredChecksPendingAt.ShouldBeInRange(before, after);
    }

    [Fact]
    public void WhenCreatedFromRevisionInProgress_RaisesIssueChecksPendingDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();

        // Act
        revisionInProgress.MarkChecksPending();

        // Assert
        IssueChecksPending domainEvent = revisionInProgress.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueChecksPending>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(revisionInProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenCreatedFromRevisionInProgress_ViaMarkUnchangedChecksPending_CopiesSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();

        // Act
        ChecksPendingIssue checksPending = revisionInProgress.MarkUnchangedChecksPending();

        // Assert
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.Id.ShouldBe(revisionInProgress.Id),
            () => checksPending.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => checksPending.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => checksPending.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl));
    }

    [Fact]
    public void WhenCreatedFromRevisionInProgress_ViaMarkUnchangedChecksPending_RaisesIssueChecksPendingDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();

        // Act
        revisionInProgress.MarkUnchangedChecksPending();

        // Assert
        IssueChecksPending domainEvent = revisionInProgress.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueChecksPending>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(revisionInProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
