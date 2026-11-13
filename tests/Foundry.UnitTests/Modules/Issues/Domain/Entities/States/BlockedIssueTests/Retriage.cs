using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.BlockedIssueTests;

public sealed class Retriage
{
    [Fact]
    public void WhenRetriaged_GoesBackToTriageKeepingItsBlockers()
    {
        // Arrange
        BlockedIssue blocked = new IssueBuilder().Detected().Block([4, 6]);

        // Act
        TriageQueuedIssue triageQueued = blocked.Retriage(DateTimeOffset.UtcNow);

        // Assert
        triageQueued.ShouldSatisfyAllConditions(
            () => triageQueued.BlockedBy.ShouldBe([4, 6]),
            () => blocked.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueTriageQueued>());
    }
}
