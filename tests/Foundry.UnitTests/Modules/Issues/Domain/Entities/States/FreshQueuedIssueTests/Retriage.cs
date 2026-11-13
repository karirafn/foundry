using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.FreshQueuedIssueTests;

public sealed class Retriage
{
    [Fact]
    public void WhenRetriaged_GoesBackToTriage()
    {
        // Arrange
        FreshQueuedIssue queued = new IssueBuilder().FreshQueued();

        // Act
        TriageQueuedIssue triageQueued = queued.Retriage(DateTimeOffset.UtcNow);

        // Assert
        triageQueued.ShouldSatisfyAllConditions(
            () => triageQueued.Id.ShouldBe(queued.Id),
            () => queued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueTriageQueued>());
    }
}
