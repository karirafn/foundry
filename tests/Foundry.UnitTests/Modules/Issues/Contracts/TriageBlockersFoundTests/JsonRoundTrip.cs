using System.Text.Json;

using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.TriageBlockersFoundTests;

public sealed class JsonRoundTrip
{
    [Fact]
    public void WhenRoundTrippedThroughTheOutbox_KeepsEveryFieldAndTheBlockerOrder()
    {
        // Arrange
        TriageBlockersFound original = new(
            Guid.NewGuid(),
            MonitoredRepositoryId.New(),
            42,
            WorkerRunId.New(),
            [new TriageBlocker(31, "needs the schema"), new TriageBlocker(15, "needs the retry policy")]);

        // Act
        OutboxMessage message = OutboxMessage.Create(original, DateTimeOffset.UtcNow);
        System.Type? eventType = System.Type.GetType(message.Type);
        eventType.ShouldNotBeNull();
        TriageBlockersFound? deserialized = JsonSerializer.Deserialize(
            message.Payload,
            eventType,
            OutboxSerializerOptions.Default) as TriageBlockersFound;

        // Assert
        TriageBlockersFound roundTripped = deserialized.ShouldNotBeNull();
        roundTripped.ShouldSatisfyAllConditions(
            () => roundTripped.IssueId.ShouldBe(original.IssueId),
            () => roundTripped.MonitoredRepositoryId.ShouldBe(original.MonitoredRepositoryId),
            () => roundTripped.IssueNumber.ShouldBe(original.IssueNumber),
            () => roundTripped.WorkerRunId.ShouldBe(original.WorkerRunId),
            () => roundTripped.Blockers.ShouldBe(original.Blockers));
    }
}
