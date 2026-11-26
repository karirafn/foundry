using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

/// <summary>
/// The Before Ready rounds (ADR 0128) start again only when new code reaches the branch; every other transition
/// carries them.
/// </summary>
public sealed class BeforeReadyRounds
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly RevisionTrigger.ReviewFindings Findings =
        new([], [], ReviewTestsAssessment.Missing, "No test covers the change.");

    [Fact]
    public void WhenANewIssueIsDetected_ItHasSpentNoRounds()
    {
        // Arrange
        IssueBuilder builder = new();

        // Act
        DetectedIssue detected = builder.Detected();

        // Assert
        detected.BeforeReadyRounds.ShouldBe(0);
    }

    [Fact]
    public void WhenTheReviewerRequestsChanges_TheFindingsRevisionStartsTheRoundsAgain()
    {
        // Arrange
        ReviewInProgressIssue reviewing = ((ReviewQueuedIssue)ChecksPendingWithARoundSpent()
                .ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, reviewPullRequests: true, Head, Now))
            .Claim(WorkerRunId.New(), Now);

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.ChangesRequested, patchId: null, Findings, Now);

        // Assert
        reviewing.BeforeReadyRounds.ShouldBe(1);
        next.ShouldBeOfType<RevisionQueuedIssue>().BeforeReadyRounds.ShouldBe(0);
    }

    [Fact]
    public void WhenTheReviewApprovesAgain_TheRoundsAreCarriedIntoTheNextRebase()
    {
        // Arrange
        ReviewInProgressIssue reviewing = ((ReviewQueuedIssue)ChecksPendingWithARoundSpent()
                .ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, reviewPullRequests: true, Head, Now))
            .Claim(WorkerRunId.New(), Now);

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.Approved, "patch-1", Findings, Now);

        // Assert
        next.ShouldBeOfType<RebasePendingIssue>().BeforeReadyRounds.ShouldBe(1);
    }

    [Fact]
    public void WhenTheChecksFail_TheChecksFixRevisionStartsTheRoundsAgain()
    {
        // Arrange
        ChecksPendingIssue checksPending = ChecksPendingWithARoundSpent();
        ChecksOutcome red = new ChecksOutcome.StillFailing([new FailingCheck("ci/build", "FAILURE", null)]);

        // Act
        Issue next = checksPending.ResolveVerdict(red, autoMerge: false, reviewPullRequests: false, Head, Now);

        // Assert
        next.ShouldBeOfType<RevisionQueuedIssue>().BeforeReadyRounds.ShouldBe(0);
    }

    [Fact]
    public void WhenAPersonGivesFeedbackInReview_TheFeedbackRevisionStartsTheRoundsAgain()
    {
        // Arrange
        ReviewIssue review = ReviewWithARoundSpent();

        // Act
        RevisionQueuedIssue revision = review.Revise([new ReviewComment("Please rename this.")], Now);

        // Assert
        review.BeforeReadyRounds.ShouldBe(1);
        revision.BeforeReadyRounds.ShouldBe(0);
    }

    [Fact]
    public void WhenAPersonGivesFeedbackWhileTheChecksRun_TheFeedbackRevisionStartsTheRoundsAgain()
    {
        // Arrange
        ChecksPendingIssue checksPending = ChecksPendingWithARoundSpent();

        // Act
        RevisionQueuedIssue revision = checksPending.Revise([new ReviewComment("Please rename this.")], Now);

        // Assert
        revision.BeforeReadyRounds.ShouldBe(0);
    }

    [Fact]
    public void WhenAPersonAnswersARevisionsQuestion_TheResumedRevisionStartsTheRoundsAgain()
    {
        // Arrange
        AwaitingInputIssue awaiting = ((RevisionQueuedIssue)new IssueBuilder().RebasePendingBeforeReady()
                .ResolveRebase(new RebaseOutcome.Conflict(), Now))
            .Claim(WorkerRunId.New(), Now)
            .MarkAwaitingInput(IssueBuilder.DefaultQuestions, Now, Now);
        awaiting.RecordQuestionPosted(commentId: 1001, Now, commentUrl: null);

        // Act
        RevisionQueuedIssue revision = (RevisionQueuedIssue)awaiting.ProvideInput(
            ClarificationInput.FromComments("Use Postgres."),
            Now.AddMinutes(1),
            Now.AddMinutes(1));

        // Assert
        awaiting.BeforeReadyRounds.ShouldBe(1);
        revision.ShouldSatisfyAllConditions(
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.InputProvided>(),
            () => revision.BeforeReadyRounds.ShouldBe(0));
    }

    [Fact]
    public void WhenTheSpecIsRewritten_TheSpecChangedRevisionStartsTheRoundsAgain()
    {
        // Arrange
        TriageInProgressIssue triaging = ReviewWithARoundSpent().RetriageRewrittenSpec(Now).Claim(WorkerRunId.New(), Now);

        // Act
        Issue next = triaging.MarkReady(new TriageVerdict.Ready(IssueKind.Feature, Now), [], Now);

        // Assert
        triaging.BeforeReadyRounds.ShouldBe(1);
        RevisionQueuedIssue revision = next.ShouldBeOfType<RevisionQueuedIssue>();
        revision.ShouldSatisfyAllConditions(
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.SpecChanged>(),
            () => revision.BeforeReadyRounds.ShouldBe(0));
    }

    [Fact]
    public void WhenAnImplementRunOpensAPullRequest_TheRoundsStartAgain()
    {
        // Arrange
        InProgressIssue inProgress = ReviewWithARoundSpent()
            .Fail("The pull request was closed.", FailureCategory.PrClosed, Now)
            .Retry(Now)
            .Claim(WorkerRunId.New(), Now);

        // Act
        RebasePendingIssue rebasePending = inProgress.MarkRebasePending(
            "feat/1-thing",
            "https://github.com/owner/repo/pull/8",
            Now,
            Now);

        // Assert
        inProgress.BeforeReadyRounds.ShouldBe(1);
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBe(new RebasePendingReason.AfterRun()),
            () => rebasePending.BeforeReadyRounds.ShouldBe(0));
    }

    [Fact]
    public void WhenAPersonPushesWhileInReview_TheRecheckStartsTheRoundsAgain()
    {
        // Arrange
        ReviewIssue review = ReviewWithARoundSpent();

        // Act
        ChecksPendingIssue checksPending = review.RecheckAfterHeadMoved(Now);

        // Assert
        checksPending.BeforeReadyRounds.ShouldBe(0);
    }

    [Fact]
    public void WhenTheChecksPassOnARerun_TheRecheckCarriesTheRounds()
    {
        // Arrange
        ReviewIssue review = (ReviewIssue)new IssueBuilder()
            .WithChecksOutcome(new ChecksOutcome.NoneDetected())
            .RebasePendingBeforeReady()
            .ResolveRebase(new RebaseOutcome.Disabled(), Now);

        // Act
        ChecksPendingIssue? checksPending = review.RecheckAfterChecksPassed(Now);

        // Assert
        checksPending.ShouldNotBeNull().BeforeReadyRounds.ShouldBe(1);
    }

    [Fact]
    public void WhenTheRebaseConflicts_TheConflictRevisionCarriesTheRounds()
    {
        // Arrange
        RebasePendingIssue rebasePending = (RebasePendingIssue)ChecksPendingWithARoundSpent()
            .ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, reviewPullRequests: false, Head, Now);

        // Act
        Issue next = rebasePending.ResolveRebase(new RebaseOutcome.Conflict(), Now);

        // Assert
        next.ShouldBeOfType<RevisionQueuedIssue>().BeforeReadyRounds.ShouldBe(2);
    }

    private static ChecksPendingIssue ChecksPendingWithARoundSpent() =>
        (ChecksPendingIssue)new IssueBuilder().WithVerifiedHeadSha(Head).RebasePendingBeforeReady()
            .ResolveRebase(new RebaseOutcome.Rebased(), Now);

    private static ReviewIssue ReviewWithARoundSpent() =>
        (ReviewIssue)new IssueBuilder().WithVerifiedHeadSha(Head).RebasePendingBeforeReady()
            .ResolveRebase(new RebaseOutcome.Disabled(), Now);
}
