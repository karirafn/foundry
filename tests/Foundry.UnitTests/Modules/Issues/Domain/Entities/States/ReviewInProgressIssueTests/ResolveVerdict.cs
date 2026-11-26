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

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewInProgressIssueTests;

public sealed class ResolveVerdict
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly RevisionTrigger.ReviewFindings NoFindings =
        new([], [], ReviewTestsAssessment.Adequate, "Covered.");

    [Fact]
    public void WhenApprovedOnPassedChecksWithAutoMerge_ReturnsMergePendingPinnedToTheReviewedCommit()
    {
        // Arrange
        WorkerRunId reviewRun = WorkerRunId.New();
        ReviewInProgressIssue reviewing = new IssueBuilder()
            .WithWorkerRunId(reviewRun)
            .WithVerifiedHeadSha(Head)
            .WithAutoMerge(true)
            .ReviewInProgress();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.Approved, patchId: null, NoFindings, DateTimeOffset.UtcNow);

        // Assert
        MergePendingIssue mergePending = next.ShouldBeOfType<MergePendingIssue>();
        mergePending.ShouldSatisfyAllConditions(
            () => mergePending.HeadSha.ShouldBe(Head),
            () => mergePending.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>(),
            () => mergePending.BranchName.ShouldBe(reviewing.BranchName),
            () => mergePending.PullRequestUrl.ShouldBe(reviewing.PullRequestUrl),
            () => mergePending.WorkerRunId.ShouldBe(reviewRun),
            () => reviewing.DomainEvents.OfType<IssueMergePending>().ShouldHaveSingleItem());
    }

    [Fact]
    public void WhenApprovedWithoutAutoMerge_RebasesBeforeReady()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder().WithVerifiedHeadSha(Head).WithAutoMerge(false).ReviewInProgress();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.Approved, patchId: null, NoFindings, DateTimeOffset.UtcNow);

        // Assert
        RebasePendingIssue rebasePending = next.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.VerifiedHeadSha.ShouldBe(Head),
            () => rebasePending.ReviewOutcome.ShouldBeNull(),
            () => reviewing.DomainEvents.OfType<IssueRebasePending>().ShouldHaveSingleItem(),
            () => reviewing.DomainEvents.OfType<IssueInReview>().ShouldBeEmpty());
    }

    [Fact]
    public void WhenApprovedOnNoChecksWithAutoMerge_RebasesBeforeReadyBecauseAPullRequestWithoutChecksNeverMergesItself()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder()
            .WithChecksOutcome(new ChecksOutcome.NoneDetected())
            .WithAutoMerge(true)
            .ReviewInProgress();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.Approved, patchId: null, NoFindings, DateTimeOffset.UtcNow);

        // Assert
        RebasePendingIssue rebasePending = next.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.ChecksOutcome.ShouldBeOfType<ChecksOutcome.NoneDetected>());
    }

    [Fact]
    public void WhenApprovedAfterPersonFeedback_RebasesBeforeReady()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithReviewComments([new ReviewComment("Please rename this.")])
            .ChecksPendingFromRevision();
        ReviewInProgressIssue reviewing = ((ReviewQueuedIssue)checksPending.ResolveVerdict(
                new ChecksOutcome.Passed(),
                autoMerge: true,
                reviewPullRequests: true,
                Head,
                DateTimeOffset.UtcNow))
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.Approved, patchId: null, NoFindings, DateTimeOffset.UtcNow);

        // Assert
        RebasePendingIssue rebasePending = next.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.HasPersonFeedback.ShouldBeTrue());
    }

    [Fact]
    public void WhenApprovedForPerson_RebasesBeforeReadyEvenWithAutoMerge()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder().WithAutoMerge(true).ReviewInProgress();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.ApprovedForPerson, patchId: null, NoFindings, DateTimeOffset.UtcNow);

        // Assert
        RebasePendingIssue rebasePending = next.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.ReviewOutcome.ShouldBeNull());
    }

    [Fact]
    public void WhenChangesRequestedWithFixRunsLeft_QueuesAFindingsFixOnTheSameBranch()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder().WithAutoMerge(true).ReviewInProgress();
        RevisionTrigger.ReviewFindings findings = Findings();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.ChangesRequested, patchId: null, findings, DateTimeOffset.UtcNow);

        // Assert
        RevisionQueuedIssue queued = next.ShouldBeOfType<RevisionQueuedIssue>();
        queued.ShouldSatisfyAllConditions(
            () => queued.Trigger.ShouldBeSameAs(findings),
            () => queued.ReviewFixAttempts.ShouldBe(1),
            () => queued.BranchName.ShouldBe(reviewing.BranchName),
            () => queued.PullRequestUrl.ShouldBe(reviewing.PullRequestUrl),
            () => queued.PriorFeedbackCutoffAt.ShouldBe(reviewing.FeedbackCutoffAt),
            () => queued.RunKind.ShouldBe(RunKind.FindingsFix),
            () => reviewing.DomainEvents.OfType<IssueRevisionQueued>().ShouldHaveSingleItem());
    }

    [Fact]
    public void WhenChangesRequested_TheFindingsFixLeavesHasPersonFeedbackUnset()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder().WithAutoMerge(true).ReviewInProgress();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.ChangesRequested, patchId: null, Findings(), DateTimeOffset.UtcNow);

        // Assert
        next.HasPersonFeedback.ShouldBeFalse();
    }

    [Fact]
    public void WhenChangesRequestedAfterPersonFeedback_TheFindingsFixKeepsHasPersonFeedback()
    {
        // Arrange
        ReviewInProgressIssue reviewing = ReviewingAfterPersonFeedback();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.ChangesRequested, patchId: null, Findings(), DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<RevisionQueuedIssue>().HasPersonFeedback.ShouldBeTrue();
    }

    [Fact]
    public void WhenChangesRequestedAgainAfterOneFindingsFix_QueuesTheSecondFindingsFix()
    {
        // Arrange
        ReviewInProgressIssue reviewing = ReviewingAfterFindingsFixes(new IssueBuilder().ReviewInProgress(), fixes: 1);

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.ChangesRequested, patchId: null, Findings(), DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<RevisionQueuedIssue>().ReviewFixAttempts.ShouldBe(2);
    }

    [Fact]
    public void WhenChangesRequestedWithTheFixBudgetSpent_ReturnsReview()
    {
        // Arrange
        ReviewInProgressIssue reviewing = ReviewingAfterFindingsFixes(
            new IssueBuilder().WithAutoMerge(true).ReviewInProgress(),
            fixes: Issue.ReviewMaxFixRuns);

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.ChangesRequested, patchId: null, Findings(), DateTimeOffset.UtcNow);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.ReviewOutcome.ShouldBeNull(),
            () => review.ReviewFixAttempts.ShouldBe(Issue.ReviewMaxFixRuns),
            () => reviewing.DomainEvents.OfType<IssueInReview>().ShouldHaveSingleItem());
    }

    [Fact]
    public void WhenApprovedAfterAFindingsFix_TheBudgetStaysSpent()
    {
        // Arrange
        ReviewInProgressIssue reviewing = ReviewingAfterFindingsFixes(
            new IssueBuilder().WithAutoMerge(true).ReviewInProgress(),
            fixes: 1);

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.Approved, patchId: null, NoFindings, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<MergePendingIssue>().ReviewFixAttempts.ShouldBe(1);
    }

    private static RevisionTrigger.ReviewFindings Findings() =>
        new(
            [new ReviewFinding("src/a.cs", 3, ReviewSeverity.Blocking, ReviewCategory.Correctness, null, "Off by one.")],
            [new ReviewUnmetCriterion("AC1", "Retries on 5xx.", "No retry path.")],
            ReviewTestsAssessment.Missing,
            "No test covers the retry.");

    private static ReviewInProgressIssue ReviewingAfterPersonFeedback()
    {
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithReviewComments([new ReviewComment("Please rename this.")])
            .ChecksPendingFromRevision();
        return ReviewAgain(checksPending);
    }

    // Each fix runs, publishes and passes checks, so the next review sees the spent budget carried through.
    private static ReviewInProgressIssue ReviewingAfterFindingsFixes(ReviewInProgressIssue reviewing, int fixes)
    {
        for (int i = 0; i < fixes; i++)
        {
            RevisionQueuedIssue queued = (RevisionQueuedIssue)reviewing.ResolveVerdict(
                ReviewVerdictOutcome.ChangesRequested,
                patchId: null,
                Findings(),
                DateTimeOffset.UtcNow);
            ChecksPendingIssue checksPending = queued
                .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow)
                .MarkRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                .MarkChecksPending(new RebaseOutcome.UpToDate(), DateTimeOffset.UtcNow);
            reviewing = ReviewAgain(checksPending);
        }

        return reviewing;
    }

    private static ReviewInProgressIssue ReviewAgain(ChecksPendingIssue checksPending) =>
        ((ReviewQueuedIssue)checksPending.ResolveVerdict(
                new ChecksOutcome.Passed(),
                autoMerge: true,
                reviewPullRequests: true,
                Head,
                DateTimeOffset.UtcNow))
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);
}
