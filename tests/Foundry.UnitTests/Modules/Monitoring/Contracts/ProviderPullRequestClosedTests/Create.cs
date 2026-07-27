using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.ProviderPullRequestClosedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        ProviderPullRequestClosed @event = new(repositoryId, IssueNumber: 12);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.RepositoryId.ShouldBe(repositoryId);
        @event.IssueNumber.ShouldBe(12);
    }
}
