using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RebasePendingIssueTests;

public sealed class FromInProgress
{
    [Fact]
    public void WhenCreatedFromInProgress_CopiesSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .InProgress();

        // Act
        RebasePendingIssue rebasePending = inProgress.MarkRebasePending(
            "feat/1-test",
            "https://github.com/owner/repo/pull/1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        // Assert
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Id.ShouldBe(inProgress.Id),
            () => rebasePending.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => rebasePending.IssueNumber.ShouldBe(inProgress.IssueNumber),
            () => rebasePending.Title.ShouldBe(inProgress.Title),
            () => rebasePending.Author.ShouldBe(inProgress.Author),
            () => rebasePending.Url.ShouldBe(inProgress.Url),
            () => rebasePending.DetectedAt.ShouldBe(inProgress.DetectedAt));
    }

    [Fact]
    public void WhenCreatedFromInProgress_SetsWorkRunId_BranchName_PullRequestUrl()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .InProgress();
        string branchName = "feat/1-test";
        string pullRequestUrl = "https://github.com/owner/repo/pull/1";

        // Act
        RebasePendingIssue rebasePending = inProgress.MarkRebasePending(
            branchName,
            pullRequestUrl,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        // Assert
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => rebasePending.BranchName.ShouldBe(branchName),
            () => rebasePending.PullRequestUrl.ShouldBe(pullRequestUrl));
    }

    [Fact]
    public void WhenCreatedFromInProgress_SetsFeedbackCutoffAt()
    {
        // Arrange
        DateTimeOffset feedbackCutoffAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .InProgress();

        // Act
        RebasePendingIssue rebasePending = inProgress.MarkRebasePending(
            "feat/1-test",
            "https://github.com/owner/repo/pull/1",
            feedbackCutoffAt,
            DateTimeOffset.UtcNow);

        // Assert
        rebasePending.FeedbackCutoffAt.ShouldBe(feedbackCutoffAt);
    }

    [Fact]
    public void WhenCreatedFromInProgress_SetsEnteredRebasePendingAt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .InProgress();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        RebasePendingIssue rebasePending = inProgress.MarkRebasePending(
            "feat/1-test",
            "https://github.com/owner/repo/pull/1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        rebasePending.EnteredRebasePendingAt.ShouldBeInRange(before, after);
    }

    [Fact]
    public void WhenCreatedFromInProgress_RaisesIssueRebasePendingDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .InProgress();

        // Act
        inProgress.MarkRebasePending(
            "feat/1-test",
            "https://github.com/owner/repo/pull/1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        // Assert
        IssueRebasePending domainEvent = inProgress.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueRebasePending>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(inProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
