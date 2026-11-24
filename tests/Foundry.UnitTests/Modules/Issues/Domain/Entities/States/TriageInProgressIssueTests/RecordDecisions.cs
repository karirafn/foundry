using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageInProgressIssueTests;

public sealed class RecordDecisions
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenAReadyVerdictCarriesDecisions_TheQueuedIssueLogsADecidedRound()
    {
        // Arrange
        TriageInProgressIssue triaging = new IssueBuilder().TriageInProgress();
        TriageDecision decision = TriageDecisionBuilder.Build();

        // Act
        Issue next = triaging.MarkReady(new TriageVerdict.Ready(IssueKind.Feature, DecidedAt), [decision], DateTimeOffset.UtcNow);

        // Assert
        FreshQueuedIssue queued = next.ShouldBeOfType<FreshQueuedIssue>();
        queued.ShouldSatisfyAllConditions(
            () => queued.Clarifications.ShouldBe([Clarification.Decide([decision], DecidedAt)]),
            () => queued.OpenClarification.ShouldBeNull());
    }

    [Fact]
    public void WhenAReadyVerdictWithDecisionsMeetsOpenBlockers_TheBlockedIssueLogsTheDecidedRound()
    {
        // Arrange
        TriageQueuedIssue queuedForTriage = new IssueBuilder().TriageQueued();
        queuedForTriage.SetBlockedBy([3]);
        TriageInProgressIssue triaging = queuedForTriage.Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);
        TriageDecision decision = TriageDecisionBuilder.Build();

        // Act
        Issue next = triaging.MarkReady(new TriageVerdict.Ready(IssueKind.Feature, DecidedAt), [decision], DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<BlockedIssue>().Clarifications.ShouldBe([Clarification.Decide([decision], DecidedAt)]);
    }

    [Fact]
    public void WhenAReadyVerdictCarriesNoDecisions_TheLogIsUnchanged()
    {
        // Arrange
        TriageInProgressIssue triaging = new IssueBuilder().TriageInProgress();

        // Act
        Issue next = triaging.MarkReady(new TriageVerdict.Ready(IssueKind.Feature, DecidedAt), [], DateTimeOffset.UtcNow);

        // Assert
        next.Clarifications.ShouldBeEmpty();
    }

    [Fact]
    public void WhenANeedsInputVerdictCarriesDecisions_TheOpenRoundCarriesThem()
    {
        // Arrange
        TriageInProgressIssue triaging = new IssueBuilder().TriageInProgress();
        TriageDecision decision = TriageDecisionBuilder.Build();
        IReadOnlyList<InputQuestion> questions = [StructuredQuestionBuilder.Build()];

        // Act
        AwaitingInputIssue awaiting = triaging.MarkNeedsInput(
            new TriageVerdict.NeedsInput(IssueKind.Feature, questions, DecidedAt),
            [decision],
            DateTimeOffset.UtcNow);

        // Assert
        Clarification.Asked open = awaiting.OpenClarification.ShouldNotBeNull();
        open.ShouldSatisfyAllConditions(
            () => open.Questions.ShouldBe(questions),
            () => open.Decisions.ShouldBe([decision]),
            () => awaiting.Clarifications.ShouldHaveSingleItem().ShouldBeSameAs(open));
    }
}
