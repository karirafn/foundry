using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.OutboxMessageTests;

public sealed class RecordFailure
{
    [Fact]
    public void WhenRecordFailureCalled_AttemptsIncrementsByOne()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(MakeEvent(), DateTimeOffset.UtcNow);

        // Act
        message.RecordFailure("Connection refused");

        // Assert
        message.Attempts.ShouldBe(1);
    }

    [Fact]
    public void WhenRecordFailureCalledTwice_AttemptsEqualsTwo()
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
    public void WhenRecordFailureCalled_SetsErrorToProvidedMessage()
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
    public void WhenRecordFailureCalledTwice_ErrorReflectsSecondMessage()
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
    public void WhenRecordFailureCalled_ProcessedAtRemainsNull()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(MakeEvent(), DateTimeOffset.UtcNow);

        // Act
        message.RecordFailure("some error");

        // Assert
        message.ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public void WhenErrorExceedsMaxLength_TruncatesToMaxLength()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(MakeEvent(), DateTimeOffset.UtcNow);
        string longError = new('x', 2001);

        // Act
        message.RecordFailure(longError);

        // Assert
        message.Error.ShouldNotBeNull();
        message.Error.Length.ShouldBe(2000);
    }

    [Fact]
    public void WhenErrorIsExactlyMaxLength_StoresUnchanged()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(MakeEvent(), DateTimeOffset.UtcNow);
        string exactError = new('x', 2000);

        // Act
        message.RecordFailure(exactError);

        // Assert
        message.Error.ShouldBe(exactError);
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
