using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.BlockerLinkFailedTests;

public sealed class JsonRoundTrip
{
    [Fact]
    public void WhenRoundTrippedThroughTheOutbox_KeepsEveryField()
    {
        // Arrange
        BlockerLinkFailed original = new(
            MonitoredRepositoryId.New(),
            42,
            Guid.NewGuid(),
            15,
            "Issue links are not available on this plan",
            new DateTimeOffset(2026, 11, 12, 9, 30, 0, TimeSpan.Zero));

        // Act
        OutboxMessage message = OutboxMessage.Create(original, DateTimeOffset.UtcNow);
        System.Type? eventType = System.Type.GetType(message.Type);
        eventType.ShouldNotBeNull();
        BlockerLinkFailed? deserialized = JsonSerializer.Deserialize(
            message.Payload,
            eventType,
            OutboxSerializerOptions.Default) as BlockerLinkFailed;

        // Assert
        deserialized.ShouldNotBeNull().ShouldBe(original);
    }
}
