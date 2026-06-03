using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.IssueEligibilityCheckedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        EligibilityViolationInfo violation = new("branch-protection:allow-direct-pushes", "Direct pushes are allowed.");

        // Act
        IssueEligibilityChecked @event = new(
            repositoryId,
            IssueNumber: 42,
            Violations: [violation]);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.MonitoredRepositoryId.ShouldBe(repositoryId);
        @event.IssueNumber.ShouldBe(42);
        @event.Violations.Count.ShouldBe(1);
    }
}
