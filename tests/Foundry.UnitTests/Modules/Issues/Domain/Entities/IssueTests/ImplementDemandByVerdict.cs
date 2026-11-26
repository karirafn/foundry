using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

public sealed class ImplementDemandByVerdict
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly DateTimeOffset Now = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);

    public enum Verdict
    {
        None,
        ReadyRoutine,
        ReadyDemanding,
        NeedsInput,
        Blocked,
    }

    [Theory]
    [InlineData(Verdict.None, false, ImplementDemand.Demanding)]
    [InlineData(Verdict.ReadyRoutine, false, ImplementDemand.Routine)]
    [InlineData(Verdict.ReadyDemanding, false, ImplementDemand.Demanding)]
    [InlineData(Verdict.NeedsInput, false, ImplementDemand.Demanding)]
    [InlineData(Verdict.Blocked, false, ImplementDemand.Demanding)]
    [InlineData(Verdict.None, true, ImplementDemand.Demanding)]
    [InlineData(Verdict.ReadyRoutine, true, ImplementDemand.Demanding)]
    [InlineData(Verdict.ReadyDemanding, true, ImplementDemand.Demanding)]
    [InlineData(Verdict.NeedsInput, true, ImplementDemand.Demanding)]
    [InlineData(Verdict.Blocked, true, ImplementDemand.Demanding)]
    public void TheImplementDemandFollowsTheEscalationThenTheLatestReadyVerdict(
        Verdict verdict,
        bool escalated,
        ImplementDemand expected)
    {
        // Arrange
        Issue issue = IssueWith(verdict, escalated);

        // Act
        ImplementDemand demand = issue.ImplementDemand;

        // Assert
        issue.ShouldSatisfyAllConditions(
            () => issue.DemandEscalated.ShouldBe(escalated),
            () => demand.ShouldBe(expected));
    }

    private static Issue IssueWith(Verdict verdict, bool escalated)
    {
        if (verdict is Verdict.None)
        {
            return escalated ? new IssueBuilder().RevisionQueuedFromReviewFindings() : new IssueBuilder().FreshQueued();
        }

        TriageInProgressIssue triaging = escalated ? EscalatedRetriage() : new IssueBuilder().TriageInProgress();

        return verdict switch
        {
            Verdict.ReadyRoutine => triaging.MarkReady(new TriageVerdict.Ready(IssueKind.Feature, Now, ImplementDemand.Routine, "none"), [], Now),
            Verdict.ReadyDemanding => triaging.MarkReady(new TriageVerdict.Ready(IssueKind.Feature, Now, ImplementDemand.Demanding, "A migration."), [], Now),
            Verdict.NeedsInput => triaging.MarkNeedsInput(new TriageVerdict.NeedsInput(IssueKind.Feature, IssueBuilder.DefaultQuestions, Now), [], Now),
            Verdict.Blocked => triaging.MarkBlocked(
                new TriageVerdict.Blocked(IssueKind.Feature, [new TriageBlocker(3, "Needs the API first.")], Now),
                Now),
            _ => throw new ArgumentOutOfRangeException(nameof(verdict)),
        };
    }

    private static TriageInProgressIssue EscalatedRetriage() =>
        new IssueBuilder()
            .RevisionQueuedFromReviewFindings()
            .Claim(WorkerRunId.New(), Now)
            .MarkRebasePending(Now, Now)
            .MarkChecksPending(new RebaseOutcome.UpToDate(), Now)
            .ToReview(new ChecksOutcome.Passed(), Head, Now)
            .RetriageRewrittenSpec(Now)
            .Claim(WorkerRunId.New(), Now);
}
