using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.BlockedIssueTests;

public sealed class FromDetected
{
    [Fact]
    public void WhenCalled_ReturnedBlockedIssueHasSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected();
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
        DetectedIssue detected = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected();
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
        DetectedIssue detected = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected();
        IReadOnlyList<int> blockers = [42, 99];

        // Act
        BlockedIssue blocked = BlockedIssue.FromDetected(detected, blockers);

        // Assert
        blocked.BlockedBy.ShouldBe(blockers);
    }
}
