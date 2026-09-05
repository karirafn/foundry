using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.IssueDetailsChangedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        IssueDetailsChanged @event = new(
            repositoryId,
            IssueNumber: 42,
            Title: "Updated title",
            Labels: ["enhancement"]);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.IssueNumber.ShouldBe(42);
        @event.MonitoredRepositoryId.ShouldBe(repositoryId);
    }
}
