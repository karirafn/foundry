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

public sealed class MarkReadyWithBranchOnlyOpenWork
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly OpenWork Work = OpenWork.OnBranch("feat/7-add-retry");

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
    public void WhenAnIssueGoesBackToTriageHoldingOnlyABranch_TriageQueuedHoldsIt()
    {
        // Act
        TriageQueuedIssue queued = TriageQueuedIssue.HoldingOpenWork(new IssueBuilder().TriageQueued(), Work, Now);

        // Assert
        queued.OpenWork.ShouldBe(Work);
        queued.OpenWork!.PullRequestUrl.ShouldBeNull();
    }

    [Fact]
    public void WhenItHasNoOpenBlockers_QueuesAContinuationOnTheOpenWorkBranch()
    {
        // Arrange
        TriageInProgressIssue triaging = Triaging(new IssueBuilder());
        TriageVerdict.Ready verdict = new(IssueKind.Feature, Now);

        // Act
        Issue next = triaging.MarkReady(verdict, [], Now);

        // Assert
        ContinuationQueuedIssue continuation = next.ShouldBeOfType<ContinuationQueuedIssue>();
        continuation.ShouldSatisfyAllConditions(
            () => continuation.BranchName.ShouldBe(Work.BranchName),
            () => continuation.FailureReason.ShouldBeEmpty(),
            () => continuation.TriageVerdict.ShouldBe(verdict),
            () => continuation.OpenWork.ShouldBeNull(),
            () => triaging.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuationQueued>());
    }

    [Fact]
    public void WhenItHasOpenBlockers_HoldsTheContinuationBlocked()
    {
        // Arrange
        TriageInProgressIssue triaging = Triaging(new IssueBuilder(), blockers: [3]);

        // Act
        Issue next = triaging.MarkReady(new TriageVerdict.Ready(IssueKind.Feature, Now), [], Now);

        // Assert
        ContinuationBlockedIssue blocked = next.ShouldBeOfType<ContinuationBlockedIssue>();
        blocked.ShouldSatisfyAllConditions(
            () => blocked.BranchName.ShouldBe(Work.BranchName),
            () => blocked.FailureReason.ShouldBeEmpty(),
            () => blocked.BlockedBy.ShouldBe([3]),
            () => blocked.OpenWork.ShouldBeNull(),
            () => triaging.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuationBlocked>());
    }

    [Fact]
    public void WhenTheTitleAndTheTriagedKindChanged_TheContinuationKeepsTheOriginalBranch()
    {
        // Arrange
        TriageInProgressIssue triaging = Triaging(new IssueBuilder()
            .WithTitle("Add retry")
            .WithKindClassification(KindClassification.Unclassified));
        triaging.UpdateDetails("Rework webhook delivery entirely", [], KindClassification.Unclassified);

        // Act
        Issue next = triaging.MarkReady(new TriageVerdict.Ready(IssueKind.Refactor, Now), [], Now);

        // Assert
        next.ShouldBeOfType<ContinuationQueuedIssue>().DispatchBranchName!.Value.Value.ShouldBe("feat/7-add-retry");
    }
}
