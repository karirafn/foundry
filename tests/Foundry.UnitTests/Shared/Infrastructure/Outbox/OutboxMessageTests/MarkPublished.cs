using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.OutboxMessageTests;

public sealed class MarkPublished
{
    [Fact]
    public void WhenCalled_SetsProcessedAt()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(MakeEvent(), DateTimeOffset.UtcNow);
        DateTimeOffset publishedAt = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        message.MarkPublished(publishedAt);

        // Assert
        message.ProcessedAt.ShouldBe(publishedAt);
    }

    [Fact]
    public void WhenCalled_DoesNotChangeAttempts()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(MakeEvent(), DateTimeOffset.UtcNow);
        int attemptsBefore = message.Attempts;

        // Act
        message.MarkPublished(DateTimeOffset.UtcNow);

        // Assert
        message.Attempts.ShouldBe(attemptsBefore);
    }

    private static IssueDetected MakeEvent() =>
        new(
            MonitoredRepositoryId.From(Guid.NewGuid()),
            1,
            "Title",
            "user",
            "https://github.com/org/repo/issues/1",
            ["bug"],
            "claude",
            DateTimeOffset.UtcNow);
}
