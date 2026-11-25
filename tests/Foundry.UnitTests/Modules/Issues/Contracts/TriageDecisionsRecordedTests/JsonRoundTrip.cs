using System.Text.Json;

using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.TriageDecisionsRecordedTests;

public sealed class JsonRoundTrip
{
    [Fact]
    public void WhenRoundTrippedThroughTheOutbox_KeepsEveryFieldAndTheDecisionOrder()
    {
        // Arrange
        TriageDecisionsRecorded original = new(
            Guid.NewGuid(),
            MonitoredRepositoryId.New(),
            42,
            [
                TriageDecisionBuilder.Build(question: "Which cap?"),
                TriageDecisionBuilder.Build(
                    question: "Which log level?",
                    decision: "Warning",
                    ground: TriageDecisionGround.ImplementationDetail,
                    evidence: ["src/Log.cs:3 logs warnings", "src/Log.cs:9 never logs errors"]),
            ],
            TriageRelease.WaitingOnBlockers);

        // Act
        OutboxMessage message = OutboxMessage.Create(original, DateTimeOffset.UtcNow);
        System.Type? eventType = System.Type.GetType(message.Type);
        eventType.ShouldNotBeNull();
        TriageDecisionsRecorded? deserialized = JsonSerializer.Deserialize(
            message.Payload,
            eventType,
            OutboxSerializerOptions.Default) as TriageDecisionsRecorded;

        // Assert
        TriageDecisionsRecorded roundTripped = deserialized.ShouldNotBeNull();
        roundTripped.ShouldSatisfyAllConditions(
            () => roundTripped.IssueId.ShouldBe(original.IssueId),
            () => roundTripped.MonitoredRepositoryId.ShouldBe(original.MonitoredRepositoryId),
            () => roundTripped.IssueNumber.ShouldBe(original.IssueNumber),
            () => roundTripped.Decisions.ShouldBe(original.Decisions),
            () => roundTripped.Release.ShouldBe(original.Release));
    }
}
