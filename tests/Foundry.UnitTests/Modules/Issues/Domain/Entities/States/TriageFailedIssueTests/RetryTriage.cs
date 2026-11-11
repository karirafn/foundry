using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageFailedIssueTests;

public sealed class RetryTriage
{
    [Fact]
    public void WhenRetried_GoesBackToTriage()
    {
        // Arrange
        TriageFailedIssue failed = new IssueBuilder().TriageFailed();

        // Act
        TriageQueuedIssue queued = failed.RetryTriage();

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.Id.ShouldBe(failed.Id),
            () => failed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueTriageQueued>());
    }
}
