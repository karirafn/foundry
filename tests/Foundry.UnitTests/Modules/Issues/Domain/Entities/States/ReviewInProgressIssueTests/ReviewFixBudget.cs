using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewInProgressIssueTests;

public sealed class ReviewFixBudget
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly RevisionTrigger.ReviewFindings Findings =
        new([], [new ReviewUnmetCriterion("AC1", "Retries on 5xx.", "No retry path.")], ReviewTestsAssessment.Adequate, "Covered.");

    [Fact]
    public void WhenAPersonsReviewFeedbackDrivesARevision_TheReviewFixBudgetStartsOver()
    {
        // Arrange
        ReviewIssue review = ReviewWithBudgetSpent(new ChecksOutcome.Passed());

        // Act
        RevisionQueuedIssue revision = review.Revise([new ReviewComment("Please rename this.")], DateTimeOffset.UtcNow);

        // Assert
        revision.ShouldSatisfyAllConditions(
            () => review.ReviewFixAttempts.ShouldBe(Issue.ReviewMaxFixRuns),
            () => revision.ReviewFixAttempts.ShouldBe(0));
    }

    [Fact]
    public void WhenThePullRequestHeadMovesInReview_TheReviewFixBudgetStartsOver()
    {
        // Arrange
        ReviewIssue review = ReviewWithBudgetSpent(new ChecksOutcome.Passed());

        // Act
        ChecksPendingIssue checksPending = review.RecheckAfterHeadMoved(DateTimeOffset.UtcNow);

        // Assert
        checksPending.ReviewFixAttempts.ShouldBe(0);
    }

    [Fact]
    public void WhenChecksPassOnARerunInReview_TheReviewFixBudgetCarriesOn()
    {
        // Arrange
        ReviewIssue review = ReviewWithBudgetSpent(new ChecksOutcome.NoneDetected());

        // Act
        ChecksPendingIssue? checksPending = review.RecheckAfterChecksPassed(DateTimeOffset.UtcNow);

        // Assert
        checksPending.ShouldNotBeNull().ReviewFixAttempts.ShouldBe(Issue.ReviewMaxFixRuns);
    }

    [Fact]
    public void WhenAFindingsFixRunFails_TheSpentReviewFixCarriesThroughTheRetry()
    {
        // Arrange
        RevisionQueuedIssue queued = (RevisionQueuedIssue)new IssueBuilder()
            .ReviewInProgress()
            .ResolveVerdict(ReviewVerdictOutcome.ChangesRequested, patchId: null, Findings, DateTimeOffset.UtcNow);
        RevisionFailedIssue failed = queued
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow)
            .MarkFailed("Container exited with code 1", FailureCategory.NonZeroExit, DateTimeOffset.UtcNow);

        // Act
        RevisionQueuedIssue retried = failed.Retry(DateTimeOffset.UtcNow);

        // Assert
        retried.ShouldSatisfyAllConditions(
            () => failed.ReviewFixAttempts.ShouldBe(1),
            () => retried.ReviewFixAttempts.ShouldBe(1),
            () => retried.Trigger.ShouldBeSameAs(Findings));
    }

    private static ReviewIssue ReviewWithBudgetSpent(ChecksOutcome checksOutcome)
    {
        ReviewInProgressIssue reviewing = new IssueBuilder().WithChecksOutcome(checksOutcome).ReviewInProgress();

        for (int i = 0; i < Issue.ReviewMaxFixRuns; i++)
        {
            ChecksPendingIssue checksPending = ((RevisionQueuedIssue)reviewing.ResolveVerdict(
                    ReviewVerdictOutcome.ChangesRequested,
                    patchId: null,
                    Findings,
                    DateTimeOffset.UtcNow))
                .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow)
                .MarkRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                .MarkChecksPending(new RebaseOutcome.UpToDate(), DateTimeOffset.UtcNow);
            reviewing = ((ReviewQueuedIssue)checksPending.ResolveVerdict(
                    checksOutcome,
                    autoMerge: false,
                    reviewPullRequests: true,
                    Head,
                    DateTimeOffset.UtcNow))
                .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);
        }

        return (ReviewIssue)reviewing.ResolveVerdict(ReviewVerdictOutcome.ChangesRequested, patchId: null, Findings, DateTimeOffset.UtcNow);
    }
}
