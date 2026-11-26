using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RebasePendingIssueTests;

/// <summary>
/// The Before Ready rebase (ADR 0128): how each rebase outcome resolves, how it spends the issue's rounds, and the
/// check-only pass once the rounds are spent.
/// </summary>
public sealed class BeforeReady
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";
    private const string ReviewedHead = "9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b";

    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenUpToDate_EntersReviewWithTheChecksOutcomeAndHeadItArrivedWith()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder()
            .WithChecksOutcome(new ChecksOutcome.NoneDetected())
            .WithVerifiedHeadSha(Head)
            .RebasePendingBeforeReady();

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.UpToDate(), Now);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.ChecksOutcome.ShouldBe(new ChecksOutcome.NoneDetected()),
            () => review.VerifiedHeadSha.ShouldBe(Head),
            () => review.RebaseOutcome.ShouldBe(new RebaseOutcome.UpToDate()),
            () => review.ReviewOutcome.ShouldBeNull(),
            () => review.BeforeReadyRounds.ShouldBe(0),
            () => rebasePending.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInReview>());
    }

    [Fact]
    public void WhenUpToDateAfterAReviewThatFailedOpen_EntersReviewWithTheSkippedReviewAndTheReviewedCommit()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder()
            .WithVerifiedHeadSha(ReviewedHead)
            .WithAutoMerge(true)
            .RebasePendingBeforeReadyFromSkippedReview();

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.UpToDate(), Now);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.ReviewOutcome.ShouldBe(new ReviewOutcome.Skipped(FailureCategory.VerdictInvalid.Value)),
            () => review.VerifiedHeadSha.ShouldBe(ReviewedHead),
            () => review.ChecksOutcome.ShouldBe(new ChecksOutcome.Passed()),
            () => review.AutoMerge.ShouldBeTrue());
    }

    [Fact]
    public void WhenDisabled_EntersReviewRecordingDisabledAndSpendsARound()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().WithVerifiedHeadSha(Head).RebasePendingBeforeReady();

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.Disabled(), Now);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.RebaseOutcome.ShouldBe(new RebaseOutcome.Disabled()),
            () => review.ChecksOutcome.ShouldBe(new ChecksOutcome.Passed()),
            () => review.VerifiedHeadSha.ShouldBe(Head),
            () => review.BeforeReadyRounds.ShouldBe(1));
    }

    [Fact]
    public void WhenRebased_RunsTheChecksAgainForTheBeforeReadyReasonAndSpendsARound()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePendingBeforeReady();

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.Rebased(), Now);

        // Assert
        ChecksPendingIssue checksPending = next.ShouldBeOfType<ChecksPendingIssue>();
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.Reason.ShouldBe(new ChecksPendingReason.BeforeReady()),
            () => checksPending.RebaseOutcome.ShouldBe(new RebaseOutcome.Rebased()),
            () => checksPending.BeforeReadyRounds.ShouldBe(1),
            () => rebasePending.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueChecksPending>());
    }

    [Fact]
    public void WhenConflictBelowTheCap_QueuesAConflictRevisionAndSpendsARound()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePendingBeforeReady();

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.Conflict(), Now);

        // Assert
        RevisionQueuedIssue revision = next.ShouldBeOfType<RevisionQueuedIssue>();
        revision.ShouldSatisfyAllConditions(
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.RebaseConflict>(),
            () => revision.RebaseConflictAttempts.ShouldBe(1),
            () => revision.BeforeReadyRounds.ShouldBe(1));
    }

    [Fact]
    public void WhenConflictAtTheCap_EntersReviewRecordingTheConflictAndSpendsARound()
    {
        // Arrange
        RebasePendingIssue rebasePending = (RebasePendingIssue)((ChecksPendingIssue)new IssueBuilder()
                .RebasePendingAfterConflicts(RebasePendingIssue.MaxRebaseConflictAttempts)
                .ResolveRebase(new RebaseOutcome.Conflict(), Now))
            .ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, reviewPullRequests: false, Head, Now);

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.Conflict(), Now);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.RebaseOutcome.ShouldBe(new RebaseOutcome.Conflict()),
            () => review.RebaseConflictAttempts.ShouldBe(RebasePendingIssue.MaxRebaseConflictAttempts),
            () => review.VerifiedHeadSha.ShouldBe(Head),
            () => review.BeforeReadyRounds.ShouldBe(1));
    }

    [Fact]
    public void WhenSkipped_EntersReviewRecordingTheSkipReasonAndSpendsARound()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePendingBeforeReady();

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.Skipped(RebaseSkipReasons.Timeout), Now);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.RebaseOutcome.ShouldBe(new RebaseOutcome.Skipped(RebaseSkipReasons.Timeout)),
            () => review.BeforeReadyRounds.ShouldBe(1));
    }

    [Fact]
    public void WhenMarkedChecksPendingWithoutARebase_Throws()
    {
        // Arrange
        RebasePendingIssue rebasePending = new IssueBuilder().RebasePendingBeforeReady();

        // Act
        Action act = () => rebasePending.MarkChecksPending(new RebaseOutcome.UpToDate(), Now);

        // Assert
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void WhenRoundsRemain_TheReasonIsNotCheckOnly()
    {
        // Arrange
        RebasePendingIssue rebasePending = RebasedTimes(Issue.MaxBeforeReadyRounds - 1);

        // Act
        RebasePendingReason reason = rebasePending.Reason;

        // Assert
        reason.ShouldBe(new RebasePendingReason.BeforeReady(CheckOnly: false));
    }

    [Fact]
    public void WhenTheRoundsAreSpent_TheReasonIsCheckOnly()
    {
        // Arrange
        RebasePendingIssue rebasePending = RebasedTimes(Issue.MaxBeforeReadyRounds);

        // Act
        RebasePendingReason reason = rebasePending.Reason;

        // Assert
        rebasePending.ShouldSatisfyAllConditions(
            () => reason.ShouldBe(new RebasePendingReason.BeforeReady(CheckOnly: true)),
            () => rebasePending.BeforeReadyRounds.ShouldBe(Issue.MaxBeforeReadyRounds));
    }

    [Fact]
    public void WhenCheckOnlyFindsTheBranchUpToDate_EntersReviewWithoutSpendingARound()
    {
        // Arrange
        RebasePendingIssue rebasePending = RebasedTimes(Issue.MaxBeforeReadyRounds);

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.UpToDate(), Now);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.RebaseOutcome.ShouldBe(new RebaseOutcome.UpToDate()),
            () => review.BeforeReadyRounds.ShouldBe(Issue.MaxBeforeReadyRounds));
    }

    [Fact]
    public void WhenCheckOnlyFindsTheBranchBehind_EntersReviewRecordingFellBehind()
    {
        // Arrange
        RebasePendingIssue rebasePending = RebasedTimes(Issue.MaxBeforeReadyRounds);

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.Skipped(RebaseSkipReasons.FellBehind), Now);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.RebaseOutcome.ShouldBe(new RebaseOutcome.Skipped(RebaseSkipReasons.FellBehind)),
            () => review.VerifiedHeadSha.ShouldBe(Head),
            () => review.BeforeReadyRounds.ShouldBe(Issue.MaxBeforeReadyRounds + 1));
    }

    [Fact]
    public void WhenRebasedThenConflictedThenRebased_TheNextPassIsCheckOnlyAndAFallenBehindBranchEntersReview()
    {
        // Arrange
        IssueBuilder builder = new IssueBuilder().WithVerifiedHeadSha(Head);
        ChecksPendingIssue afterFirstRebase = (ChecksPendingIssue)builder.RebasePendingBeforeReady()
            .ResolveRebase(new RebaseOutcome.Rebased(), Now);
        RevisionQueuedIssue conflictRevision = (RevisionQueuedIssue)PassChecks(afterFirstRebase)
            .ResolveRebase(new RebaseOutcome.Conflict(), Now);
        ChecksPendingIssue afterConflictFix = (ChecksPendingIssue)conflictRevision
            .Claim(WorkerRunId.New(), Now)
            .MarkRebasePending(Now, Now)
            .ResolveRebase(new RebaseOutcome.UpToDate(), Now);
        ChecksPendingIssue afterSecondRebase = (ChecksPendingIssue)PassChecks(afterConflictFix)
            .ResolveRebase(new RebaseOutcome.Rebased(), Now);
        RebasePendingIssue checkOnly = PassChecks(afterSecondRebase);

        // Act
        Issue next = checkOnly.ResolveRebase(new RebaseOutcome.Skipped(RebaseSkipReasons.FellBehind), Now);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => checkOnly.Reason.ShouldBe(new RebasePendingReason.BeforeReady(CheckOnly: true)),
            () => conflictRevision.BeforeReadyRounds.ShouldBe(2),
            () => review.RebaseOutcome.ShouldBe(new RebaseOutcome.Skipped(RebaseSkipReasons.FellBehind)));
    }

    // Each Before Ready rebase lands, so the checks run again, read green, and route the pull request back.
    private static RebasePendingIssue RebasedTimes(int rebases)
    {
        RebasePendingIssue rebasePending = new IssueBuilder().WithVerifiedHeadSha(Head).RebasePendingBeforeReady();
        for (int i = 0; i < rebases; i++)
        {
            ChecksPendingIssue checksPending = (ChecksPendingIssue)rebasePending.ResolveRebase(new RebaseOutcome.Rebased(), Now);
            rebasePending = PassChecks(checksPending);
        }

        return rebasePending;
    }

    private static RebasePendingIssue PassChecks(ChecksPendingIssue checksPending) =>
        (RebasePendingIssue)checksPending.ResolveVerdict(
            new ChecksOutcome.Passed(),
            autoMerge: false,
            reviewPullRequests: false,
            Head,
            Now);
}
