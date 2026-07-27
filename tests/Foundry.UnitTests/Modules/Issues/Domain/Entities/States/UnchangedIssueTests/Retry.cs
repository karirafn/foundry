using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.UnchangedIssueTests;

public sealed class Retry
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    private static UnchangedIssue CreateUnchangedIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Test Issue",
            body: "Test body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry"],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = detected.Enqueue();
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        return inProgress.MarkUnchanged(Guid.NewGuid());
    }

    [Fact]
    public void WhenRetried_ReturnsQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        UnchangedIssue unchanged = CreateUnchangedIssue(repositoryId);

        // Act
        QueuedIssue queued = unchanged.Retry();

        // Assert
        queued.Id.ShouldBe(unchanged.Id);
    }

    [Fact]
    public void WhenRetried_RaisesIssueQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        UnchangedIssue unchanged = CreateUnchangedIssue(repositoryId);

        // Act
        unchanged.Retry();

        // Assert
        IssueQueued domainEvent = unchanged.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(unchanged.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenRetried_QueuedIssueHasSameSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        UnchangedIssue unchanged = CreateUnchangedIssue(repositoryId);

        // Act
        QueuedIssue queued = unchanged.Retry();

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.MonitoredRepositoryId.ShouldBe(unchanged.MonitoredRepositoryId),
            () => queued.IssueNumber.ShouldBe(unchanged.IssueNumber),
            () => queued.Title.ShouldBe(unchanged.Title),
            () => queued.Body.ShouldBe(unchanged.Body),
            () => queued.DetectedAt.ShouldBe(unchanged.DetectedAt));
    }
}
