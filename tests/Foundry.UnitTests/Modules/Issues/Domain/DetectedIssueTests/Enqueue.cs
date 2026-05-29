using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.DetectedIssueTests;

public sealed class Enqueue
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static DetectedIssue CreateDetectedIssue(MonitoredRepositoryId repositoryId) =>
        DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Test Issue",
            body: "Test body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry"],
            detectedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void WhenEnqueued_ReturnsQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);

        // Act
        QueuedIssue queued = detected.Enqueue();

        // Assert
        queued.Id.ShouldBe(detected.Id);
    }

    [Fact]
    public void WhenEnqueued_RaisesIssueQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);

        // Act
        detected.Enqueue();

        // Assert
        IssueQueued domainEvent = detected.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(detected.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenEnqueued_QueuedIssueHasSameSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);

        // Act
        QueuedIssue queued = detected.Enqueue();

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.MonitoredRepositoryId.ShouldBe(detected.MonitoredRepositoryId),
            () => queued.IssueNumber.ShouldBe(detected.IssueNumber),
            () => queued.Title.ShouldBe(detected.Title),
            () => queued.Body.ShouldBe(detected.Body),
            () => queued.Author.ShouldBe(detected.Author),
            () => queued.Url.ShouldBe(detected.Url),
            () => queued.Labels.ShouldBe(detected.Labels),
            () => queued.DetectedAt.ShouldBe(detected.DetectedAt));
    }

    [Fact]
    public void WhenBlockedByIsNotEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);
        detected.SetBlockedBy([5, 10]);

        // Act / Assert
        Should.Throw<InvalidOperationException>(() => detected.Enqueue());
    }
}
