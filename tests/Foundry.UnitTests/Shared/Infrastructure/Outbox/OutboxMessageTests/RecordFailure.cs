using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.OutboxMessageTests;

public sealed class RecordFailure
{
    [Fact]
    public void WhenCalled_IncrementsAttempts()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(MakeEvent(), DateTimeOffset.UtcNow);

        // Act
        message.RecordFailure("Connection refused");

        // Assert
        message.Attempts.ShouldBe(1);
    }

    [Fact]
    public void WhenCalledTwice_IncrementsTwice()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(MakeEvent(), DateTimeOffset.UtcNow);

        // Act
        message.RecordFailure("first error");
        message.RecordFailure("second error");

        // Assert
        message.Attempts.ShouldBe(2);
    }

    [Fact]
    public void WhenCalled_SetsError()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(MakeEvent(), DateTimeOffset.UtcNow);
        string error = "Handler threw NullReferenceException";

        // Act
        message.RecordFailure(error);

        // Assert
        message.Error.ShouldBe(error);
    }

    [Fact]
    public void WhenCalledTwice_OverwritesErrorWithLatest()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(MakeEvent(), DateTimeOffset.UtcNow);

        // Act
        message.RecordFailure("first error");
        message.RecordFailure("second error");

        // Assert
        message.Error.ShouldBe("second error");
    }

    [Fact]
    public void WhenCalled_DoesNotSetProcessedAt()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(MakeEvent(), DateTimeOffset.UtcNow);

        // Act
        message.RecordFailure("some error");

        // Assert
        message.ProcessedAt.ShouldBeNull();
    }

    private static IssueDetected MakeEvent() =>
        new(
            MonitoredRepositoryId.From(Guid.NewGuid()),
            1,
            "Title",
            "Body",
            "user",
            "https://github.com/org/repo/issues/1",
            ["bug"],
            "claude",
            DateTimeOffset.UtcNow);
}
