using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ChecksPendingIssueTests;

public sealed class Complete
{
    [Fact]
    public void WhenCompleted_ReturnsCompletedIssueWithSameId()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();
        DateTimeOffset completedAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        CompletedIssue completed = checksPending.Complete(completedAt);

        // Assert
        completed.Id.ShouldBe(checksPending.Id);
    }

    [Fact]
    public void WhenCompleted_CarriesBranchNameAndPullRequestUrl()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithBranchName("feat/42-add-feature")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/42")
            .ChecksPending();
        DateTimeOffset completedAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        CompletedIssue completed = checksPending.Complete(completedAt);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.BranchName.ShouldBe(checksPending.BranchName),
            () => completed.PullRequestUrl.ShouldBe(checksPending.PullRequestUrl));
    }

    [Fact]
    public void WhenCompleted_SetsCompletedAt()
    {
        // Arrange
        DateTimeOffset completedAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        CompletedIssue completed = checksPending.Complete(completedAt);

        // Assert
        completed.CompletedAt.ShouldBe(completedAt);
    }

    [Fact]
    public void WhenCompleted_RaisesIssueCompletedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .ChecksPending();
        DateTimeOffset completedAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        checksPending.Complete(completedAt);

        // Assert
        IssueCompleted domainEvent = checksPending.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueCompleted>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(checksPending.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
