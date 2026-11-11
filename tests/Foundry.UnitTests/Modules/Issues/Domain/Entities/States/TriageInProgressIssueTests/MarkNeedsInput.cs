using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageInProgressIssueTests;

public sealed class MarkNeedsInput
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenTriageNeedsInput_AwaitsInputToRetriageWithTheQuestionsAsked()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        TriageInProgressIssue triaging = new IssueBuilder().WithWorkerRunId(workerRunId).TriageInProgress();
        IReadOnlyList<InputQuestion> questions = [new InputQuestion("Which API?", "Two exist.")];
        TriageVerdict.NeedsInput verdict = new(IssueKind.Feature, questions, DecidedAt);

        // Act
        AwaitingInputIssue awaiting = triaging.MarkNeedsInput(verdict);

        // Assert
        awaiting.ShouldSatisfyAllConditions(
            () => awaiting.ResumeTarget.ShouldBeOfType<ResumeTarget.Retriage>(),
            () => awaiting.WorkerRunId.ShouldBe(workerRunId),
            () => awaiting.Questions.ShouldBe(questions),
            () => awaiting.OpenClarification.ShouldNotBeNull().AskedAt.ShouldBe(DecidedAt),
            () => awaiting.TriageVerdict.ShouldBe(verdict),
            () => awaiting.LinkedPullRequestUrl.ShouldBeNull(),
            () => awaiting.KindClassification.ShouldBe(new KindClassification.Triaged(IssueKind.Feature)),
            () => triaging.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueAwaitingInput>());
    }
}
