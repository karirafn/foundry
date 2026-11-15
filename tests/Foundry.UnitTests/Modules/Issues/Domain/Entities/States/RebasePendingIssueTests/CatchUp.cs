using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RebasePendingIssueTests;

/// <summary>How a catch-up rebase (ADR 0111) and an after-run rebase carry and route their reason.</summary>
public sealed class CatchUp
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    [Fact]
    public void EveryRunEntry_RebasesAfterRun()
    {
        // Act
        RebasePendingIssue[] entries =
        [
            new IssueBuilder().RebasePending(),
            new IssueBuilder().RebasePendingFromRevision(),
            new IssueBuilder().RevisionInProgress().MarkUnchangedRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        ];

        // Assert
        entries.ShouldAllBe(e => e.Reason == new RebasePendingReason.AfterRun() && e.CatchUpHeadSha == null);
    }

    public static TheoryData<RebaseOutcome> SuccessfulSyncs => [new RebaseOutcome.UpToDate(), new RebaseOutcome.Rebased()];

    [Theory]
    [MemberData(nameof(SuccessfulSyncs))]
    public void WhenSynced_EntersChecksPendingForTheCatchUp(RebaseOutcome outcome)
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePendingForCatchUp();

        // Act
        Issue next = rebasePending.ResolveRebase(outcome, DateTimeOffset.UtcNow);

        // Assert
        ChecksPendingIssue checksPending = next.ShouldBeOfType<ChecksPendingIssue>();
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.Reason.ShouldBe(new ChecksPendingReason.CatchUp()),
            () => checksPending.RebaseOutcome.ShouldBe(outcome));
        rebasePending.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueChecksPending>();
    }

    [Theory]
    [MemberData(nameof(SuccessfulSyncs))]
    public void WhenAfterRunSynced_EntersChecksPendingAfterRun(RebaseOutcome outcome)
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePending();

        // Act
        Issue next = rebasePending.ResolveRebase(outcome, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ChecksPendingIssue>().Reason.ShouldBe(new ChecksPendingReason.AfterRun());
    }

    [Fact]
    public void WhenCatchUpChecksPass_RoutesBackToMergePending()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().RebasePendingForCatchUp()
            .ResolveRebase(new RebaseOutcome.Rebased(), DateTimeOffset.UtcNow)
            .ShouldBeOfType<ChecksPendingIssue>();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, "9e8d", DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<MergePendingIssue>().HeadSha.ShouldBe("9e8d");
    }

    [Fact]
    public void WhenConflictBelowCap_QueuesARebaseConflictRevision()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePendingForCatchUp();

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.Conflict(), DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<RevisionQueuedIssue>().Trigger.ShouldBeOfType<RevisionTrigger.RebaseConflict>();
    }

    public static TheoryData<RebaseOutcome> UnsyncedOutcomes =>
        [new RebaseOutcome.Disabled(), new RebaseOutcome.Skipped(RebaseSkipReasons.Timeout)];

    [Theory]
    [MemberData(nameof(UnsyncedOutcomes))]
    public void WhenSkippedOrDisabled_GoesToReviewAsCatchUpFailedKeepingTheRebaseOutcome(RebaseOutcome outcome)
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().WithVerifiedHeadSha(Head).RebasePendingForCatchUp();

        // Act
        Issue next = rebasePending.ResolveRebase(outcome, DateTimeOffset.UtcNow);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.MergeOutcome.ShouldBe(new MergeOutcome.Skipped(MergeSkipReasons.CatchUpFailed)),
            () => review.RebaseOutcome.ShouldBe(outcome),
            () => review.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>(),
            () => review.VerifiedHeadSha.ShouldBe(Head),
            () => review.BranchName.ShouldBe(rebasePending.BranchName),
            () => review.PullRequestUrl.ShouldBe(rebasePending.PullRequestUrl));
        rebasePending.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInReview>();
    }

    [Fact]
    public void WhenConflictAtCap_GoesToReviewAsCatchUpFailed()
    {
        // Arrange: the issue used every conflict revision before its catch-up.
        RebasePendingIssue atCap = new IssueBuilder().RebasePendingAfterConflicts(RebasePendingIssue.MaxRebaseConflictAttempts);
        MergePendingIssue mergePending = atCap
            .ResolveRebase(new RebaseOutcome.Conflict(), DateTimeOffset.UtcNow)
            .ShouldBeOfType<ChecksPendingIssue>()
            .ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, Head, DateTimeOffset.UtcNow)
            .ShouldBeOfType<MergePendingIssue>();
        RebasePendingIssue rebasePending = mergePending.CatchUp(DateTimeOffset.UtcNow);

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.Conflict(), DateTimeOffset.UtcNow);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.MergeOutcome.ShouldBe(new MergeOutcome.Skipped(MergeSkipReasons.CatchUpFailed));
        review.RebaseOutcome.ShouldBeOfType<RebaseOutcome.Conflict>();
    }

    [Fact]
    public void MarkChecksPending_WhenCatchUpDidNotSync_Throws()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePendingForCatchUp();

        // Act & Assert
        Should.Throw<InvalidOperationException>(
            () => rebasePending.MarkChecksPending(new RebaseOutcome.Disabled(), DateTimeOffset.UtcNow));
    }
}
