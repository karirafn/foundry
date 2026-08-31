using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewIssueTests;

public sealed class Complete
{
    [Fact]
    public void WhenCompleted_ReturnsCompletedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Review();
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        CompletedIssue completed = review.Complete(completedAt);

        // Assert
        completed.Id.ShouldBe(review.Id);
    }

    [Fact]
    public void WhenCompleted_RaisesIssueCompletedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Review();
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        review.Complete(completedAt);

        // Assert
        IssueCompleted domainEvent = review.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueCompleted>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(review.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenCompleted_CompletedIssueHasBranchAndPullRequestFromReview()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Review();
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        CompletedIssue completed = review.Complete(completedAt);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.BranchName.ShouldBe(review.BranchName),
            () => completed.PullRequestUrl.ShouldBe(review.PullRequestUrl),
            () => completed.CompletedAt.ShouldBe(completedAt),
            () => completed.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
