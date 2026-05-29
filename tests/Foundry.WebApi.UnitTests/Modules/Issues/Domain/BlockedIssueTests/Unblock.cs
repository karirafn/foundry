using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Domain.BlockedIssueTests;

public sealed class Unblock
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static BlockedIssue CreateBlockedIssue(MonitoredRepositoryId repositoryId)
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
        return BlockedIssue.FromDetected(detected, [5]);
    }

    [Fact]
    public void WhenUnblocked_ReturnsQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        BlockedIssue blocked = CreateBlockedIssue(repositoryId);

        // Act
        QueuedIssue queued = blocked.Unblock();

        // Assert
        queued.Id.ShouldBe(blocked.Id);
    }

    [Fact]
    public void WhenUnblocked_RaisesIssueQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        BlockedIssue blocked = CreateBlockedIssue(repositoryId);

        // Act
        blocked.Unblock();

        // Assert
        IssueQueued domainEvent = blocked.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(blocked.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenUnblocked_ResultingQueuedIssueHasEmptyBlockedBy()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        BlockedIssue blocked = CreateBlockedIssue(repositoryId);

        // Act
        QueuedIssue queued = blocked.Unblock();

        // Assert
        queued.BlockedBy.ShouldBeEmpty();
    }
}
