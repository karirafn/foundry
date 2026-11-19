using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

public sealed class ChecksFixBudget
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly IReadOnlyList<FailingCheck> FailingChecks =
    [
        new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1"),
    ];

    private static readonly RevisionTrigger.ReviewFindings Findings =
        new([], [new ReviewUnmetCriterion("AC1", "Retries on 5xx.", "No retry path.")], ReviewTestsAssessment.Adequate, "Covered.");

    [Fact]
    public void WhenARetriedImplementRunOpensAPullRequest_TheChecksFixBudgetStartsOver()
    {
        // Arrange
        InProgressIssue inProgress = SpentBudget()
            .Fail("Container exited with code 1", FailureCategory.NonZeroExit, Now)
            .Retry(Now)
            .Claim(WorkerRunId.New(), Now);

        // Act
        RebasePendingIssue rebasePending = inProgress.MarkRebasePending(
            "feat/1-test",
            "https://github.com/owner/repo/pull/1",
            Now,
            Now);

        // Assert
        rebasePending.ChecksFixAttempts.ShouldBe(0);
    }

    [Fact]
    public void WhenAMergePendingIssueCatchesUp_TheChecksFixBudgetStartsOver()
    {
        // Arrange
        MergePendingIssue mergePending = (MergePendingIssue)SpentBudget()
            .ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, reviewPullRequests: false, Head, Now);

        // Act
        RebasePendingIssue rebasePending = mergePending.CatchUp(Now);

        // Assert
        rebasePending.ChecksFixAttempts.ShouldBe(0);
    }

    [Fact]
    public void WhenThePullRequestHeadMovesInReview_TheChecksFixBudgetStartsOver()
    {
        // Arrange
        ReviewIssue review = SpentBudget().ToReview(new ChecksOutcome.Passed(), Head, Now);

        // Act
        ChecksPendingIssue checksPending = review.RecheckAfterHeadMoved(Now);

        // Assert
        checksPending.ChecksFixAttempts.ShouldBe(0);
    }

    [Fact]
    public void WhenAPersonsReviewFeedbackInReviewDrivesARevision_TheChecksFixBudgetStartsOver()
    {
        // Arrange
        ReviewIssue review = SpentBudget().ToReview(new ChecksOutcome.Passed(), Head, Now);

        // Act
        RevisionQueuedIssue revision = review.Revise([new ReviewComment("Please rename this.")], Now);

        // Assert
        revision.ChecksFixAttempts.ShouldBe(0);
    }

    [Fact]
    public void WhenAPersonsReviewFeedbackWhileMergePendingDrivesARevision_TheChecksFixBudgetStartsOver()
    {
        // Arrange
        MergePendingIssue mergePending = (MergePendingIssue)SpentBudget()
            .ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, reviewPullRequests: false, Head, Now);

        // Act
        RevisionQueuedIssue revision = mergePending.Revise([new ReviewComment("Please rename this.")], Now);

        // Assert
        revision.ChecksFixAttempts.ShouldBe(0);
    }

    [Fact]
    public void WhenTheReviewerRequestsChanges_TheFindingsFixGetsAFreshChecksFixBudget()
    {
        // Arrange
        ReviewInProgressIssue reviewing = ((ReviewQueuedIssue)SpentBudget()
                .ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, reviewPullRequests: true, Head, Now))
            .Claim(WorkerRunId.New(), Now);

        // Act
        RevisionQueuedIssue revision = (RevisionQueuedIssue)reviewing.ResolveVerdict(
            ReviewVerdictOutcome.ChangesRequested,
            Findings,
            Now);

        // Assert
        revision.ChecksFixAttempts.ShouldBe(0);
    }

    [Fact]
    public void WhenAFixRunAsksAndIsAnswered_TheResumedRevisionGetsAFreshChecksFixBudget()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder()
            .ChecksPendingFromFixChecks()
            .FixChecks(FailingChecks, Now)
            .Claim(WorkerRunId.New(), Now)
            .MarkAwaitingInput(IssueBuilder.DefaultQuestions, Now, Now);
        awaiting.RecordQuestionPosted(commentId: 1001, Now, commentUrl: null);

        // Act
        RevisionQueuedIssue revision = (RevisionQueuedIssue)awaiting.ProvideInput(
            ClarificationInput.FromComments("Use Postgres."),
            Now.AddMinutes(1),
            Now.AddMinutes(1));

        // Assert
        revision.ChecksFixAttempts.ShouldBe(0);
    }

    [Fact]
    public void WhenAFixRunFailsAndIsRetried_TheSpentChecksFixesCarryThroughTheRetry()
    {
        // Arrange
        RevisionFailedIssue failed = new IssueBuilder()
            .ChecksPendingFromFixChecks()
            .FixChecks(FailingChecks, Now)
            .Claim(WorkerRunId.New(), Now)
            .MarkFailed("Container exited with code 1", FailureCategory.NonZeroExit, Now);

        // Act
        RevisionQueuedIssue retried = failed.Retry(Now);

        // Assert
        retried.ChecksFixAttempts.ShouldBe(ChecksPendingIssue.ChecksMaxFixPushes);
    }

    private static ChecksPendingIssue SpentBudget()
    {
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPendingAtCap();
        checksPending.ChecksFixAttempts.ShouldBe(ChecksPendingIssue.ChecksMaxFixPushes);
        return checksPending;
    }
}
