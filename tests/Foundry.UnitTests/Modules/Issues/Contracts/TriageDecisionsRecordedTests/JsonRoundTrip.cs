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
            TriageRelease.WaitingOnBlockers,
            ImplementDemand.Routine,
            "Copies the existing slice.");

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
            () => roundTripped.Release.ShouldBe(original.Release),
            () => roundTripped.Demand.ShouldBe(ImplementDemand.Routine),
            () => roundTripped.DemandReason.ShouldBe("Copies the existing slice."));
    }

    [Fact]
    public void WhenAPayloadFromBeforeTheDemandIsRead_ReadsDemandingWithNoReason()
    {
        // Arrange
        const string Stored =
            """{"IssueId":"7d0c1f5e-2b8a-4c3e-9f1d-0a2b3c4d5e6f","MonitoredRepositoryId":{"Value":"1a2b3c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d"},"IssueNumber":42,"Decisions":[],"Release":1}""";

        // Act
        TriageDecisionsRecorded? deserialized =
            JsonSerializer.Deserialize<TriageDecisionsRecorded>(Stored, OutboxSerializerOptions.Default);

        // Assert
        TriageDecisionsRecorded read = deserialized.ShouldNotBeNull();
        read.ShouldSatisfyAllConditions(
            () => read.IssueNumber.ShouldBe(42),
            () => read.Release.ShouldBe(TriageRelease.WaitingOnBlockers),
            () => read.Demand.ShouldBe(ImplementDemand.Demanding),
            () => read.DemandReason.ShouldBeNull());
    }
}
