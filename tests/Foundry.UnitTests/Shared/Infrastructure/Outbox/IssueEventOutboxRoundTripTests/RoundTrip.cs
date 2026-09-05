using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.IssueEventOutboxRoundTripTests;

/// <summary>
/// Proves that Issues module integration events serialize and deserialize symmetrically
/// through the outbox path (OutboxMessage.Create → OutboxSerializerOptions.Default).
/// The WorkerRunId [JsonConverter] stores the id as a flat UUID string on the wire.
/// </summary>
public sealed class RoundTrip
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void WhenClaimSkippedIsCreated_RoundTripsThroughOutbox()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        ClaimSkipped original = new(workerRunId);

        // Act
        OutboxMessage message = OutboxMessage.Create(original, Now);
        System.Type? eventType = System.Type.GetType(message.Type);
        eventType.ShouldNotBeNull();
        ClaimSkipped? deserialized = JsonSerializer.Deserialize(
            message.Payload,
            eventType,
            OutboxSerializerOptions.Default) as ClaimSkipped;

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.WorkerRunId.ShouldBe(original.WorkerRunId);
    }
}
