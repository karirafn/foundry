using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;

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

    [Fact]
    public void WhenWorkerRunCompletedCarriesAnInputRequest_RoundTripsThroughOutbox()
    {
        // Arrange
        WorkerRunCompleted original = new(
            WorkerRunId.New(),
            Guid.NewGuid(),
            "feat/1-test",
            null,
            WorkerRunMergeState.None,
            Now,
            new WorkerInputRequest([StructuredQuestionBuilder.Build()], BranchHasCommits: true));

        // Act
        WorkerRunCompleted? deserialized = RoundTripThroughOutbox(original);

        // Assert
        WorkerInputRequest request = deserialized.ShouldNotBeNull().InputRequest.ShouldNotBeNull();
        request.ShouldSatisfyAllConditions(
            () => request.BranchHasCommits.ShouldBeTrue(),
            () => request.Questions.ShouldBe(original.InputRequest!.Questions));
    }

    [Fact]
    public void WhenAClaimCarriesAClarificationLog_RoundTripsThroughOutbox()
    {
        // Arrange
        Clarification answered = Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", "Postgres.")], Now)
            .Answer(ClarificationInput.FromComments("Use Postgres."), Now.AddHours(1));
        IssueClaimed original = new(new ClaimedIssueDispatch(
            IssueId.New(),
            WorkerRunId.New(),
            1,
            "Title",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            Foundry.Shared.BranchName.From("feat/1-test"),
            Foundry.Modules.Monitoring.Contracts.MonitoredRepositoryId.New(),
            new Foundry.Modules.Monitoring.Contracts.WorkerProvider.GitHub(),
            new DispatchContext.Revision("feat/1-test", "https://github.com/owner/repo/pull/1", new RevisionTrigger.InputProvided()),
            "https://api.github.com/repos/owner/repo/issues/1",
            [answered]));

        // Act
        IssueClaimed? deserialized = RoundTripThroughOutbox(original);

        // Assert
        ClaimedIssueDispatch dispatch = deserialized.ShouldNotBeNull().Dispatch;
        dispatch.ShouldSatisfyAllConditions(
            () => dispatch.Clarifications.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(answered),
            () => dispatch.Context.ShouldBeOfType<DispatchContext.Revision>().Trigger.ShouldBeOfType<RevisionTrigger.InputProvided>());
    }

    [Fact]
    public void WhenAClaimResumesARoundContinuedWithoutAnswer_TheRefusalFlagRoundTripsThroughOutbox()
    {
        // Arrange
        IssueClaimed original = new(new ClaimedIssueDispatch(
            IssueId.New(),
            WorkerRunId.New(),
            1,
            "Title",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            Foundry.Shared.BranchName.From("feat/1-test"),
            Foundry.Modules.Monitoring.Contracts.MonitoredRepositoryId.New(),
            new Foundry.Modules.Monitoring.Contracts.WorkerProvider.GitHub(),
            new DispatchContext.Continuation("feat/1-test") { ContinuedWithoutAnswer = true },
            "https://api.github.com/repos/owner/repo/issues/1"));

        // Act
        IssueClaimed? deserialized = RoundTripThroughOutbox(original);

        // Assert
        deserialized.ShouldNotBeNull().Dispatch.Context.ShouldBeOfType<DispatchContext.Continuation>()
            .ContinuedWithoutAnswer.ShouldBeTrue();
    }

    [Fact]
    public void WhenAClaimRevisesARewrittenSpec_TheSpecChangedTriggerRoundTripsThroughOutbox()
    {
        // Arrange
        IssueClaimed original = new(new ClaimedIssueDispatch(
            IssueId.New(),
            WorkerRunId.New(),
            1,
            "Title",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            Foundry.Shared.BranchName.From("feat/1-test"),
            Foundry.Modules.Monitoring.Contracts.MonitoredRepositoryId.New(),
            new Foundry.Modules.Monitoring.Contracts.WorkerProvider.GitHub(),
            new DispatchContext.Revision("feat/1-test", "https://github.com/owner/repo/pull/1", new RevisionTrigger.SpecChanged()),
            "https://api.github.com/repos/owner/repo/issues/1"));

        // Act
        IssueClaimed? deserialized = RoundTripThroughOutbox(original);

        // Assert
        deserialized.ShouldNotBeNull().Dispatch.Context.ShouldBeOfType<DispatchContext.Revision>()
            .Trigger.ShouldBeOfType<RevisionTrigger.SpecChanged>();
    }

    [Fact]
    public void WhenATriageRunCompletesWithItsSpecHash_TheHashRoundTripsThroughOutbox()
    {
        // Arrange
        TriageRunCompleted original = new(
            Guid.NewGuid(),
            WorkerRunId.New(),
            new TriageResult.Ready(TriageKind.Feature, []),
            Now,
            SpecHashFixtures.Other);

        // Act
        TriageRunCompleted? deserialized = RoundTripThroughOutbox(original);

        // Assert
        deserialized.ShouldNotBeNull().TriagedSpecHash.ShouldBe(SpecHashFixtures.Other);
    }

    [Fact]
    public void WhenATriageRunCompletedRowHasNoSpecHash_ItReadsAsNone()
    {
        // Arrange
        TriageRunCompleted original = new(
            Guid.NewGuid(),
            WorkerRunId.New(),
            new TriageResult.Ready(TriageKind.Feature, []),
            Now);

        // Act
        TriageRunCompleted? deserialized = RoundTripThroughOutbox(original);

        // Assert
        deserialized.ShouldNotBeNull().TriagedSpecHash.ShouldBeNull();
    }

    private static IssueClaimed ClaimWithDemand(ImplementDemand? demand) =>
        new(new ClaimedIssueDispatch(
            IssueId.New(),
            WorkerRunId.New(),
            1,
            "Title",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            Foundry.Shared.BranchName.From("feat/1-test"),
            Foundry.Modules.Monitoring.Contracts.MonitoredRepositoryId.New(),
            new Foundry.Modules.Monitoring.Contracts.WorkerProvider.GitHub(),
            new DispatchContext.Fresh("feat/1-test"),
            "https://api.github.com/repos/owner/repo/issues/1",
            Demand: demand));

    [Fact]
    public void WhenAClaimCarriesADemand_ItRoundTripsAsALowercaseToken()
    {
        // Arrange
        IssueClaimed original = ClaimWithDemand(ImplementDemand.Routine);

        // Act
        OutboxMessage message = OutboxMessage.Create(original, Now);
        IssueClaimed? deserialized = RoundTripThroughOutbox(original);

        // Assert
        message.Payload.ShouldContain("\"routine\"");
        deserialized.ShouldNotBeNull().Dispatch.Demand.ShouldBe(ImplementDemand.Routine);
    }

    [Fact]
    public void WhenAClaimRowPredatesTheDemand_ItReadsAsNoDemand()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(ClaimWithDemand(ImplementDemand.Demanding), Now);
        System.Text.Json.Nodes.JsonObject payload = System.Text.Json.Nodes.JsonNode.Parse(message.Payload)!.AsObject();
        System.Text.Json.Nodes.JsonObject dispatch = payload.First(p => p.Value is System.Text.Json.Nodes.JsonObject).Value!.AsObject();
        string demandKey = dispatch.Select(p => p.Key).Single(k => string.Equals(k, "demand", StringComparison.OrdinalIgnoreCase));
        dispatch.Remove(demandKey);

        // Act
        IssueClaimed? deserialized = JsonSerializer.Deserialize<IssueClaimed>(payload.ToJsonString(), OutboxSerializerOptions.Default);

        // Assert
        deserialized.ShouldNotBeNull().Dispatch.Demand.ShouldBeNull();
    }

    private static T? RoundTripThroughOutbox<T>(T original)
        where T : class, Foundry.Shared.IIntegrationEvent
    {
        OutboxMessage message = OutboxMessage.Create(original, Now);
        System.Type? eventType = System.Type.GetType(message.Type);
        eventType.ShouldNotBeNull();
        return JsonSerializer.Deserialize(message.Payload, eventType, OutboxSerializerOptions.Default) as T;
    }
}
