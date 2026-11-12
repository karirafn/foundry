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
            new WorkerInputRequest([new InputQuestion("Which database?", "Two are configured.", "Postgres.")], BranchHasCommits: true));

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
        Clarification answered = Clarification.Ask([new InputQuestion("Which database?", "Two are configured.", "Postgres.")], Now)
            .Answer("Use Postgres.", Now.AddHours(1));
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

    private static T? RoundTripThroughOutbox<T>(T original)
        where T : class, Foundry.Shared.IIntegrationEvent
    {
        OutboxMessage message = OutboxMessage.Create(original, Now);
        System.Type? eventType = System.Type.GetType(message.Type);
        eventType.ShouldNotBeNull();
        return JsonSerializer.Deserialize(message.Payload, eventType, OutboxSerializerOptions.Default) as T;
    }
}
