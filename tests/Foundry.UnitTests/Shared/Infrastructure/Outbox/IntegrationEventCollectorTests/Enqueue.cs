using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.IntegrationEventCollectorTests;

public sealed class Enqueue
{
    [Fact]
    public void WhenEventEnqueued_CollectorHoldsPendingMessage()
    {
        // Arrange
        IntegrationEventCollector collector = new();
        IssueDetected @event = MakeEvent();

        // Act
        collector.Enqueue(@event);

        // Assert
        collector.HasPending.ShouldBeTrue();
    }

    [Fact]
    public void WhenNoEventsEnqueued_CollectorHasNoPending()
    {
        // Arrange
        IntegrationEventCollector collector = new();

        // Act / Assert
        collector.HasPending.ShouldBeFalse();
    }

    [Fact]
    public void WhenTwoEventsEnqueued_CollectorHoldsBothMessages()
    {
        // Arrange
        IntegrationEventCollector collector = new();

        // Act
        collector.Enqueue(MakeEvent());
        collector.Enqueue(MakeEvent());

        // Assert
        collector.PendingCount.ShouldBe(2);
    }

    private static IssueDetected MakeEvent() =>
        new(
            MonitoredRepositoryId.From(Guid.NewGuid()),
            42,
            "Fix the bug",
            "Some body",
            "user",
            "https://github.com/org/repo/issues/42",
            ["bug", "claude"],
            "claude",
            DateTimeOffset.UtcNow);
}
