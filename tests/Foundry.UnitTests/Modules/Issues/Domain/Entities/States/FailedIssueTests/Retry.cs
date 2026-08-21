using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.FailedIssueTests;

public sealed class Retry
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    private static FailedIssue CreateFailedIssue(MonitoredRepositoryId repositoryId)
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
        FreshQueuedIssue queued = detected.Enqueue();
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        return inProgress.MarkFailed(Guid.NewGuid(), "Container exited with code 1", DateTimeOffset.UtcNow, "generic_failure");
    }

    [Fact]
    public void WhenRetried_ReturnsQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FailedIssue failed = CreateFailedIssue(repositoryId);

        // Act
        FreshQueuedIssue queued = failed.Retry();

        // Assert
        queued.Id.ShouldBe(failed.Id);
    }

    [Fact]
    public void WhenRetried_RaisesIssueQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FailedIssue failed = CreateFailedIssue(repositoryId);

        // Act
        failed.Retry();

        // Assert
        IssueQueued domainEvent = failed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(failed.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenRetried_QueuedIssueHasSameSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FailedIssue failed = CreateFailedIssue(repositoryId);

        // Act
        FreshQueuedIssue queued = failed.Retry();

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.MonitoredRepositoryId.ShouldBe(failed.MonitoredRepositoryId),
            () => queued.IssueNumber.ShouldBe(failed.IssueNumber),
            () => queued.Title.ShouldBe(failed.Title),
            () => queued.Body.ShouldBe(failed.Body),
            () => queued.DetectedAt.ShouldBe(failed.DetectedAt));
    }
}
