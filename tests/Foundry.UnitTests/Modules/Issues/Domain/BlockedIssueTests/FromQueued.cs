using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.BlockedIssueTests;

public sealed class FromQueued
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
