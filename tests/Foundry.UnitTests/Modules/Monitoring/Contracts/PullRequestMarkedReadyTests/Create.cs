using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.PullRequestMarkedReadyTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        PullRequestMarkedReady @event = new(repositoryId, IssueNumber: 12, PullRequestUrl: "https://github.com/owner/repo/pull/13");

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.RepositoryId.ShouldBe(repositoryId);
        @event.IssueNumber.ShouldBe(12);
        @event.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/13");
    }
}
