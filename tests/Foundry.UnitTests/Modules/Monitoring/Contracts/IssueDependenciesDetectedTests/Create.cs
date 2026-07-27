using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.IssueDependenciesDetectedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        IssueDependenciesDetected @event = new(
            repositoryId,
            IssueNumber: 42,
            BlockedByIssueNumbers: [10, 11]);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.IssueNumber.ShouldBe(42);
        @event.BlockedByIssueNumbers.Count.ShouldBe(2);
    }
}
