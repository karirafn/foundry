using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.PullRequestChangesRequestedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewComment comment = new("Please fix the null check");

        // Act
        PullRequestChangesRequested @event = new(
            repositoryId,
            IssueNumber: 42,
            Comments: [comment]);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.ShouldSatisfyAllConditions(
            () => @event.RepositoryId.ShouldBe(repositoryId),
            () => @event.IssueNumber.ShouldBe(42),
            () => @event.Comments.Count.ShouldBe(1),
            () => @event.Comments[0].ShouldBe(comment));
    }
}
