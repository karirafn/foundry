using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.ProcessedEventTests;

public sealed class For
{
    [Fact]
    public void WhenCreated_StoresEventId()
    {
        // Arrange
        Guid eventId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        ProcessedEvent processed = ProcessedEvent.For(eventId, "MyHandler", now);

        // Assert
        processed.EventId.ShouldBe(eventId);
    }

    [Fact]
    public void WhenCreated_StoresHandlerName()
    {
        // Arrange
        Guid eventId = Guid.NewGuid();
        string handlerName = "Foundry.Modules.Issues.Features.CreateIssueHandler";
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        ProcessedEvent processed = ProcessedEvent.For(eventId, handlerName, now);

        // Assert
        processed.Handler.ShouldBe(handlerName);
    }

    [Fact]
    public void WhenCreated_StoresProcessedAt()
    {
        // Arrange
        Guid eventId = Guid.NewGuid();
        DateTimeOffset now = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        ProcessedEvent processed = ProcessedEvent.For(eventId, "MyHandler", now);

        // Assert
        processed.ProcessedAt.ShouldBe(now);
    }
}
