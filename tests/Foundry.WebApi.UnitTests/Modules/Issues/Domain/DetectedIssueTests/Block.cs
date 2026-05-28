using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Domain.DetectedIssueTests;

public sealed class Block
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
    public void WhenBlocked_ReturnsBlockedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);
        IReadOnlyList<int> blockers = [10];

        // Act
        BlockedIssue blocked = detected.Block(blockers);

        // Assert
        blocked.Id.ShouldBe(detected.Id);
    }

    [Fact]
    public void WhenBlocked_RaisesIssueBlockedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);
        IReadOnlyList<int> blockers = [10];

        // Act
        detected.Block(blockers);

        // Assert
        IssueBlocked domainEvent = detected.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueBlocked>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(detected.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenBlocked_BlockedByContainsSuppliedBlockers()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);
        IReadOnlyList<int> blockers = [10, 20];

        // Act
        BlockedIssue blocked = detected.Block(blockers);

        // Assert
        blocked.BlockedBy.ShouldBe(blockers);
    }

    [Fact]
    public void WhenBlockersIsEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);
        IReadOnlyList<int> blockers = [];

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => detected.Block(blockers));
    }
}
