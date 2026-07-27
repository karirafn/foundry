using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.BlockedIssueTests;

public sealed class FromQueued
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

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
    public void WhenCalled_ReturnedBlockedIssueHasSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
        IReadOnlyList<int> blockers = [7];

        // Act
        BlockedIssue blocked = BlockedIssue.FromQueued(queued, blockers);

        // Assert
        blocked.Id.ShouldBe(queued.Id);
    }

    [Fact]
    public void WhenCalled_CopiesSharedPropertiesFromQueuedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
        IReadOnlyList<int> blockers = [7];

        // Act
        BlockedIssue blocked = BlockedIssue.FromQueued(queued, blockers);

        // Assert
        blocked.ShouldSatisfyAllConditions(
            () => blocked.MonitoredRepositoryId.ShouldBe(queued.MonitoredRepositoryId),
            () => blocked.IssueNumber.ShouldBe(queued.IssueNumber),
            () => blocked.Title.ShouldBe(queued.Title),
            () => blocked.Body.ShouldBe(queued.Body),
            () => blocked.Author.ShouldBe(queued.Author),
            () => blocked.Url.ShouldBe(queued.Url),
            () => blocked.Labels.ShouldBe(queued.Labels),
            () => blocked.DetectedAt.ShouldBe(queued.DetectedAt));
    }

    [Fact]
    public void WhenCalled_BlockedByContainsSuppliedBlockers()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
        IReadOnlyList<int> blockers = [7, 13];

        // Act
        BlockedIssue blocked = BlockedIssue.FromQueued(queued, blockers);

        // Assert
        blocked.BlockedBy.ShouldBe(blockers);
    }
}
