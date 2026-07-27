using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.OutboxMessageTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_AssignsNewGuidId()
    {
        // Arrange
        IssueDetected @event = MakeEvent();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        OutboxMessage message = OutboxMessage.Create(@event, now);

        // Assert
        message.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void WhenCreatedTwice_AssignsDifferentIds()
    {
        // Arrange
        IssueDetected @event = MakeEvent();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        OutboxMessage first = OutboxMessage.Create(@event, now);
        OutboxMessage second = OutboxMessage.Create(@event, now);

        // Assert
        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    public void WhenCreated_SetsOccurredAtFromSuppliedClock()
    {
        // Arrange
        IssueDetected @event = MakeEvent();
        DateTimeOffset now = new(2025, 1, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        OutboxMessage message = OutboxMessage.Create(@event, now);

        // Assert
        message.OccurredAt.ShouldBe(now);
    }

    [Fact]
    public void WhenCreated_AttemptsIsZero()
    {
        // Arrange
        IssueDetected @event = MakeEvent();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        OutboxMessage message = OutboxMessage.Create(@event, now);

        // Assert
        message.Attempts.ShouldBe(0);
    }

    [Fact]
    public void WhenCreated_ProcessedAtIsNull()
    {
        // Arrange
        IssueDetected @event = MakeEvent();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        OutboxMessage message = OutboxMessage.Create(@event, now);

        // Assert
        message.ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public void WhenCreated_ErrorIsNull()
    {
        // Arrange
        IssueDetected @event = MakeEvent();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        OutboxMessage message = OutboxMessage.Create(@event, now);

        // Assert
        message.Error.ShouldBeNull();
    }

    [Fact]
    public void WhenCreated_TypeIsAssemblyQualifiedName()
    {
        // Arrange
        IssueDetected @event = MakeEvent();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        OutboxMessage message = OutboxMessage.Create(@event, now);

        // Assert
        message.Type.ShouldBe(typeof(IssueDetected).AssemblyQualifiedName!);
    }

    [Fact]
    public void WhenCreated_PayloadIsNotEmpty()
    {
        // Arrange
        IssueDetected @event = MakeEvent();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        OutboxMessage message = OutboxMessage.Create(@event, now);

        // Assert
        message.Payload.ShouldNotBeNullOrWhiteSpace();
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
