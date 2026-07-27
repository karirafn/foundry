using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.ProviderIssueClosedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        ProviderIssueClosed @event = new(repositoryId, IssueNumber: 7);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.RepositoryId.ShouldBe(repositoryId);
        @event.IssueNumber.ShouldBe(7);
    }
}
