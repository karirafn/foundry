using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewInProgressIssueTests;

/// <summary>A review that hands over a pull request a person could merge rebases it before review (ADR 0128).</summary>
public sealed class BeforeReadyRouting
{
    private const string ReviewedHead = "9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b";

    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly RevisionTrigger.ReviewFindings NoFindings =
        new([], [], ReviewTestsAssessment.Adequate, "Covered.");

    [Fact]
    public void WhenApprovedWithMergeOff_RebasesBeforeReadyCarryingTheReviewedCommit()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder()
            .WithVerifiedHeadSha(ReviewedHead)
            .WithAutoMerge(false)
            .ReviewInProgress();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.Approved, "patch-1", NoFindings, Now);

        // Assert
        RebasePendingIssue rebasePending = next.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBe(new RebasePendingReason.BeforeReady(CheckOnly: false)),
            () => rebasePending.VerifiedHeadSha.ShouldBe(ReviewedHead),
            () => rebasePending.ChecksOutcome.ShouldBe(new ChecksOutcome.Passed()),
            () => rebasePending.ReviewOutcome.ShouldBeNull(),
            () => rebasePending.AutoMerge.ShouldBeFalse(),
            () => rebasePending.LatestReview!.Outcome.ShouldBe(ReviewVerdictOutcome.Approved),
            () => reviewing.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRebasePending>());
    }

    [Fact]
    public void WhenApprovedForAPersonWithMergeOn_RebasesBeforeReadyKeepingTheMergeFlag()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder().WithAutoMerge(true).ReviewInProgress();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.ApprovedForPerson, "patch-1", NoFindings, Now);

        // Assert
        RebasePendingIssue rebasePending = next.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.AutoMerge.ShouldBeTrue());
    }

    [Fact]
    public void WhenApprovedOnNoChecksWithMergeOn_RebasesBeforeReady()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder()
            .WithChecksOutcome(new ChecksOutcome.NoneDetected())
            .WithAutoMerge(true)
            .ReviewInProgress();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.Approved, "patch-1", NoFindings, Now);

        // Assert
        next.ShouldBeOfType<RebasePendingIssue>().ChecksOutcome.ShouldBe(new ChecksOutcome.NoneDetected());
    }

    [Fact]
    public void WhenTheRunFailsWithNoRetry_RebasesBeforeReadyRecordingTheSkippedReview()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder().WithVerifiedHeadSha(ReviewedHead).ReviewInProgress();

        // Act
        Issue next = reviewing.MarkRunFailed("bad verdict", FailureCategory.VerdictInvalid, consecutiveAutoRetryableFailures: 0, Now);

        // Assert
        RebasePendingIssue rebasePending = next.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.ReviewOutcome.ShouldBe(new ReviewOutcome.Skipped(FailureCategory.VerdictInvalid.Value)),
            () => rebasePending.VerifiedHeadSha.ShouldBe(ReviewedHead),
            () => reviewing.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRebasePending>());
    }

    [Fact]
    public void WhenApprovedOnPassedChecksWithMergeOn_StillMerges()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder().WithAutoMerge(true).ReviewInProgress();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.Approved, "patch-1", NoFindings, Now);

        // Assert
        next.ShouldBeOfType<MergePendingIssue>();
    }
}
