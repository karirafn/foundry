using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ChecksPendingIssueTests;

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
        ChecksPendingIssue checksPending = inProgress.MarkChecksPending(
            "feat/1-test",
            "https://github.com/owner/repo/pull/1",
            DateTimeOffset.UtcNow);

        // Assert
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.Id.ShouldBe(inProgress.Id),
            () => checksPending.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => checksPending.IssueNumber.ShouldBe(inProgress.IssueNumber),
            () => checksPending.Title.ShouldBe(inProgress.Title),
            () => checksPending.Author.ShouldBe(inProgress.Author),
            () => checksPending.Url.ShouldBe(inProgress.Url),
            () => checksPending.DetectedAt.ShouldBe(inProgress.DetectedAt));
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
        ChecksPendingIssue checksPending = inProgress.MarkChecksPending(
            branchName,
            pullRequestUrl,
            DateTimeOffset.UtcNow);

        // Assert
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => checksPending.BranchName.ShouldBe(branchName),
            () => checksPending.PullRequestUrl.ShouldBe(pullRequestUrl));
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
        ChecksPendingIssue checksPending = inProgress.MarkChecksPending(
            "feat/1-test",
            "https://github.com/owner/repo/pull/1",
            feedbackCutoffAt);

        // Assert
        checksPending.FeedbackCutoffAt.ShouldBe(feedbackCutoffAt);
    }

    [Fact]
    public void WhenCreatedFromInProgress_SetsEnteredChecksPendingAt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .InProgress();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        ChecksPendingIssue checksPending = inProgress.MarkChecksPending(
            "feat/1-test",
            "https://github.com/owner/repo/pull/1",
            DateTimeOffset.UtcNow);

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        checksPending.EnteredChecksPendingAt.ShouldBeInRange(before, after);
    }

    [Fact]
    public void WhenCreatedFromInProgress_RaisesIssueChecksPendingDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .InProgress();

        // Act
        inProgress.MarkChecksPending(
            "feat/1-test",
            "https://github.com/owner/repo/pull/1",
            DateTimeOffset.UtcNow);

        // Assert
        IssueChecksPending domainEvent = inProgress.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueChecksPending>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(inProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
