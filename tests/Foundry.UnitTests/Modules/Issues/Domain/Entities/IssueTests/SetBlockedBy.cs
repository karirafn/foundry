using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

public sealed class SetBlockedBy
{
    [Fact]
    public void WhenBlockerListExceedsCap_TruncatesTo50()
    {
        // Arrange
        DetectedIssue issue = new IssueBuilder().Detected();
        IReadOnlyList<int> blockers = Enumerable.Range(1, 60).ToList();

        // Act
        issue.SetBlockedBy(blockers);

        // Assert
        issue.BlockedBy.Count.ShouldBe(50);
    }

    [Fact]
    public void WhenBlockerListExceedsCap_KeepsFirstFiftyItems()
    {
        // Arrange
        DetectedIssue issue = new IssueBuilder().Detected();
        IReadOnlyList<int> blockers = Enumerable.Range(1, 60).ToList();

        // Act
        issue.SetBlockedBy(blockers);

        // Assert
        issue.BlockedBy.ShouldBe(Enumerable.Range(1, 50).ToList());
    }
}
