using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.IssueDetectedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        IssueDetected @event = new(
            repositoryId,
            IssueNumber: 42,
            Title: "Test issue",
            Body: "Test body",
            Author: "alice",
            Url: "https://github.com/org/repo/issues/42",
            Labels: ["bug"],
            IssueKindLabel: "bug",
            DetectedAt: DateTimeOffset.UtcNow);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.IssueNumber.ShouldBe(42);
        @event.MonitoredRepositoryId.ShouldBe(repositoryId);
    }
}
