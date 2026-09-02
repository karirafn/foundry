using System.Text.Json;

using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.WorkerEventOutboxRoundTripTests;

/// <summary>
/// Pre-ship integration guard (D4, AC #6): proves each of the 5 retyped worker
/// integration events serializes and deserializes symmetrically through the outbox
/// path (OutboxMessage.Create → OutboxSerializerOptions.Default). The type-level
/// [JsonConverter] attribute on WorkerRunId applies WorkerRunIdJsonConverter
/// universally, so WorkerRunId is stored as a flat UUID string — identical to the
/// pre-refactor bare-Guid wire shape, keeping in-flight outbox rows backward-compatible.
/// The round-trip is flat and self-consistent: what goes in must come out equal.
/// </summary>
public sealed class RoundTrip
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void WhenWorkerCapacityAvailableIsCreated_RoundTripsThroughOutbox()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        WorkerCapacityAvailable original = new(workerRunId);

        // Act
        OutboxMessage message = OutboxMessage.Create(original, Now);
        System.Type? eventType = System.Type.GetType(message.Type);
        eventType.ShouldNotBeNull();
        WorkerCapacityAvailable? deserialized = JsonSerializer.Deserialize(
            message.Payload,
            eventType,
            OutboxSerializerOptions.Default) as WorkerCapacityAvailable;

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.WorkerRunId.ShouldBe(original.WorkerRunId);
    }

    [Fact]
    public void WhenWorkerRunCompletedIsCreated_RoundTripsThroughOutbox()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        WorkerRunCompleted original = new(
            workerRunId,
            Guid.NewGuid(),
            "feat/42-add-feature",
            "https://github.com/org/repo/pull/42",
            WorkerRunMergeState.Open);

        // Act
        OutboxMessage message = OutboxMessage.Create(original, Now);
        System.Type? eventType = System.Type.GetType(message.Type);
        eventType.ShouldNotBeNull();
        WorkerRunCompleted? deserialized = JsonSerializer.Deserialize(
            message.Payload,
            eventType,
            OutboxSerializerOptions.Default) as WorkerRunCompleted;

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.ShouldSatisfyAllConditions(
            () => deserialized.WorkerRunId.ShouldBe(original.WorkerRunId),
            () => deserialized.IssueId.ShouldBe(original.IssueId),
            () => deserialized.BranchName.ShouldBe(original.BranchName),
            () => deserialized.MergeState.ShouldBe(original.MergeState));
    }

    [Fact]
    public void WhenWorkerRunFailedIsCreated_RoundTripsThroughOutbox()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        WorkerRunFailed original = new(
            workerRunId,
            Guid.NewGuid(),
            "Container exited with code 1",
            FailureCategory.NonZeroExitToken);

        // Act
        OutboxMessage message = OutboxMessage.Create(original, Now);
        System.Type? eventType = System.Type.GetType(message.Type);
        eventType.ShouldNotBeNull();
        WorkerRunFailed? deserialized = JsonSerializer.Deserialize(
            message.Payload,
            eventType,
            OutboxSerializerOptions.Default) as WorkerRunFailed;

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.ShouldSatisfyAllConditions(
            () => deserialized.WorkerRunId.ShouldBe(original.WorkerRunId),
            () => deserialized.IssueId.ShouldBe(original.IssueId),
            () => deserialized.ReasonDescription.ShouldBe(original.ReasonDescription),
            () => deserialized.Category.ShouldBe(original.Category));
    }

    [Fact]
    public void WhenWorkerAuthenticationFailedIsCreated_RoundTripsThroughOutbox()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        WorkerAuthenticationFailed original = new(
            workerRunId,
            Guid.NewGuid(),
            "Token expired");

        // Act
        OutboxMessage message = OutboxMessage.Create(original, Now);
        System.Type? eventType = System.Type.GetType(message.Type);
        eventType.ShouldNotBeNull();
        WorkerAuthenticationFailed? deserialized = JsonSerializer.Deserialize(
            message.Payload,
            eventType,
            OutboxSerializerOptions.Default) as WorkerAuthenticationFailed;

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.ShouldSatisfyAllConditions(
            () => deserialized.WorkerRunId.ShouldBe(original.WorkerRunId),
            () => deserialized.IssueId.ShouldBe(original.IssueId),
            () => deserialized.Reason.ShouldBe(original.Reason));
    }

    [Fact]
    public void WhenWorkerCreditsExhaustedIsCreated_RoundTripsThroughOutbox()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        WorkerCreditsExhausted original = new(
            workerRunId,
            Guid.NewGuid());

        // Act
        OutboxMessage message = OutboxMessage.Create(original, Now);
        System.Type? eventType = System.Type.GetType(message.Type);
        eventType.ShouldNotBeNull();
        WorkerCreditsExhausted? deserialized = JsonSerializer.Deserialize(
            message.Payload,
            eventType,
            OutboxSerializerOptions.Default) as WorkerCreditsExhausted;

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.ShouldSatisfyAllConditions(
            () => deserialized.WorkerRunId.ShouldBe(original.WorkerRunId),
            () => deserialized.IssueId.ShouldBe(original.IssueId));
    }
}
