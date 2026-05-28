using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Domain.QueuedIssueTests;

public sealed class Block
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static QueuedIssue CreateQueuedIssue(MonitoredRepositoryId repositoryId)
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
        return detected.Enqueue();
    }

    [Fact]
    public void WhenBlocked_ReturnsBlockedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
        IReadOnlyList<int> blockers = [10];

        // Act
        BlockedIssue blocked = queued.Block(blockers);

        // Assert
        blocked.Id.ShouldBe(queued.Id);
    }

    [Fact]
    public void WhenBlocked_RaisesIssueBlockedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
        IReadOnlyList<int> blockers = [10];

        // Act
        queued.Block(blockers);

        // Assert
        IssueBlocked domainEvent = queued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueBlocked>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(queued.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenBlocked_BlockedByContainsSuppliedBlockers()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
        IReadOnlyList<int> blockers = [10, 20];

        // Act
        BlockedIssue blocked = queued.Block(blockers);

        // Assert
        blocked.BlockedBy.ShouldBe(blockers);
    }

    [Fact]
    public void WhenBlockersIsEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
        IReadOnlyList<int> blockers = [];

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => queued.Block(blockers));
    }
}
