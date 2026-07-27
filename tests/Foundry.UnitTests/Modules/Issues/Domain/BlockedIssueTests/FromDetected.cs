using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.BlockedIssueTests;

public sealed class FromDetected
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

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
    public void WhenCalled_ReturnedBlockedIssueHasSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);
        IReadOnlyList<int> blockers = [42, 99];

        // Act
        BlockedIssue blocked = BlockedIssue.FromDetected(detected, blockers);

        // Assert
        blocked.Id.ShouldBe(detected.Id);
    }

    [Fact]
    public void WhenCalled_CopiesSharedPropertiesFromDetectedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);
        IReadOnlyList<int> blockers = [42];

        // Act
        BlockedIssue blocked = BlockedIssue.FromDetected(detected, blockers);

        // Assert
        blocked.ShouldSatisfyAllConditions(
            () => blocked.MonitoredRepositoryId.ShouldBe(detected.MonitoredRepositoryId),
            () => blocked.IssueNumber.ShouldBe(detected.IssueNumber),
            () => blocked.Title.ShouldBe(detected.Title),
            () => blocked.Body.ShouldBe(detected.Body),
            () => blocked.Author.ShouldBe(detected.Author),
            () => blocked.Url.ShouldBe(detected.Url),
            () => blocked.Labels.ShouldBe(detected.Labels),
            () => blocked.DetectedAt.ShouldBe(detected.DetectedAt));
    }

    [Fact]
    public void WhenCalled_BlockedByContainsSuppliedBlockers()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);
        IReadOnlyList<int> blockers = [42, 99];

        // Act
        BlockedIssue blocked = BlockedIssue.FromDetected(detected, blockers);

        // Assert
        blocked.BlockedBy.ShouldBe(blockers);
    }
}
