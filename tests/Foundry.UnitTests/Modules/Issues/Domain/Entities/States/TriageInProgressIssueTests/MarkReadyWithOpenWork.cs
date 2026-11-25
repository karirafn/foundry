using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageInProgressIssueTests;

public sealed class MarkReadyWithOpenWork
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly OpenWork Work = OpenWork.Of("feat/7-add-retry", "https://github.com/o/r/pull/9");

    private static TriageInProgressIssue Triaging(IssueBuilder builder, IReadOnlyList<int>? blockers = null)
    {
        TriageQueuedIssue queued = TriageQueuedIssue.HoldingOpenWork(builder.TriageQueued(), Work, Now);
        if (blockers is not null)
        {
            queued.SetBlockedBy(blockers);
        }

        return queued.Claim(WorkerRunId.New(), Now);
    }

    [Fact]
    public void WhenItHasNoOpenBlockers_QueuesASpecChangedRevisionOnTheOpenWork()
    {
        // Arrange
        TriageInProgressIssue triaging = Triaging(new IssueBuilder());
        TriageVerdict.Ready verdict = new(IssueKind.Feature, Now);

        // Act
        Issue next = triaging.MarkReady(verdict, [], Now);

        // Assert
        RevisionQueuedIssue revision = next.ShouldBeOfType<RevisionQueuedIssue>();
        revision.ShouldSatisfyAllConditions(
            () => revision.BranchName.ShouldBe(Work.BranchName),
            () => revision.PullRequestUrl.ShouldBe(Work.PullRequestUrl),
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.SpecChanged>(),
            () => revision.TriageVerdict.ShouldBe(verdict),
            () => triaging.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionQueued>());
    }

    [Fact]
    public void WhenItHasOpenBlockers_HoldsTheSpecChangedRevisionBlocked()
    {
        // Arrange
        TriageInProgressIssue triaging = Triaging(new IssueBuilder(), blockers: [3]);

        // Act
        Issue next = triaging.MarkReady(new TriageVerdict.Ready(IssueKind.Feature, Now), [], Now);

        // Assert
        RevisionBlockedIssue blocked = next.ShouldBeOfType<RevisionBlockedIssue>();
        blocked.ShouldSatisfyAllConditions(
            () => blocked.BranchName.ShouldBe(Work.BranchName),
            () => blocked.PullRequestUrl.ShouldBe(Work.PullRequestUrl),
            () => blocked.Trigger.ShouldBeOfType<RevisionTrigger.SpecChanged>(),
            () => blocked.BlockedBy.ShouldBe([3]),
            () => triaging.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionBlocked>());
    }

    [Fact]
    public void WhenTheBlockersClose_TheHeldRevisionQueuesWithTheSameTriggerAndBranch()
    {
        // Arrange
        RevisionBlockedIssue blocked = Triaging(new IssueBuilder(), blockers: [3])
            .MarkReady(new TriageVerdict.Ready(IssueKind.Feature, Now), [], Now)
            .ShouldBeOfType<RevisionBlockedIssue>();

        // Act
        RevisionQueuedIssue queued = blocked.Unblock(Now);

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.BranchName.ShouldBe(Work.BranchName),
            () => queued.Trigger.ShouldBeOfType<RevisionTrigger.SpecChanged>());
    }

    [Fact]
    public void WhenTheTitleAndTheTriagedKindChanged_TheRevisionKeepsTheOriginalBranch()
    {
        // Arrange
        TriageInProgressIssue triaging = Triaging(new IssueBuilder()
            .WithTitle("Add retry")
            .WithKindClassification(KindClassification.Unclassified));
        triaging.UpdateDetails("Rework webhook delivery entirely", [], KindClassification.Unclassified);

        // Act
        Issue next = triaging.MarkReady(new TriageVerdict.Ready(IssueKind.Refactor, Now), [], Now);

        // Assert
        RevisionQueuedIssue revision = next.ShouldBeOfType<RevisionQueuedIssue>();
        revision.ShouldSatisfyAllConditions(
            () => revision.KindClassification.ShouldBe(new KindClassification.Triaged(IssueKind.Refactor)),
            () => revision.BranchName.ShouldBe("feat/7-add-retry"),
            () => revision.DispatchBranchName!.Value.Value.ShouldBe("feat/7-add-retry"));
    }

    [Fact]
    public void WhenTheRevisionIsQueued_ItCarriesNoFeedbackCutoff()
    {
        // Act
        Issue next = Triaging(new IssueBuilder()).MarkReady(new TriageVerdict.Ready(IssueKind.Feature, Now), [], Now);

        // Assert
        next.ShouldBeOfType<RevisionQueuedIssue>().PriorFeedbackCutoffAt.ShouldBeNull();
    }

    [Fact]
    public void WhenTheRevisionIsQueued_TheOpenWorkIsConsumed()
    {
        // Act
        Issue next = Triaging(new IssueBuilder()).MarkReady(new TriageVerdict.Ready(IssueKind.Feature, Now), [], Now);

        // Assert
        next.OpenWork.ShouldBeNull();
    }

    [Fact]
    public void WhenTriageIsSkippedAfterItFailed_TheOpenWorkResumesAsASpecChangedRevision()
    {
        // Arrange
        TriageFailedIssue failed = Triaging(new IssueBuilder()).MarkFailed("boom", FailureCategory.VerdictInvalid, Now);

        // Act
        Issue next = failed.SkipTriage(Now);

        // Assert
        RevisionQueuedIssue revision = next.ShouldBeOfType<RevisionQueuedIssue>();
        revision.ShouldSatisfyAllConditions(
            () => revision.BranchName.ShouldBe(Work.BranchName),
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.SpecChanged>());
    }

    [Fact]
    public void WhenARetriageQuestionIsContinuedWithoutAnswer_TheOpenWorkResumesAsASpecChangedRevision()
    {
        // Arrange
        AwaitingInputIssue awaiting = Triaging(new IssueBuilder()).MarkNeedsInput(
            new TriageVerdict.NeedsInput(IssueKind.Feature, IssueBuilder.DefaultQuestions, Now),
            [],
            Now);

        // Act
        Issue next = awaiting.ContinueWithoutAnswer(Now);

        // Assert
        RevisionQueuedIssue revision = next.ShouldBeOfType<RevisionQueuedIssue>();
        revision.ShouldSatisfyAllConditions(
            () => revision.BranchName.ShouldBe(Work.BranchName),
            () => revision.PullRequestUrl.ShouldBe(Work.PullRequestUrl),
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.SpecChanged>());
    }

    [Fact]
    public void WhenTriageNamesBlockers_TriageBlockedKeepsTheOpenWork()
    {
        // Act
        TriageBlockedIssue blocked = Triaging(new IssueBuilder()).MarkBlocked(
            new TriageVerdict.Blocked(IssueKind.Feature, [new TriageBlocker(3, "Needs the API first.")], Now),
            Now);

        // Assert
        blocked.OpenWork.ShouldBe(Work);
    }
}
