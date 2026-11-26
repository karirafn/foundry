using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

public sealed class DemandEscalationCarried
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly DateTimeOffset Now = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);

    private static RevisionQueuedIssue Escalated() => new IssueBuilder().RevisionQueuedFromReviewFindings();

    private static RevisionInProgressIssue InProgress() => Escalated().Claim(WorkerRunId.New(), Now);

    private static RebasePendingIssue Rebasing() => InProgress().MarkRebasePending(Now, Now);

    private static ChecksPendingIssue Checking() => Rebasing().MarkChecksPending(new RebaseOutcome.UpToDate(), Now);

    private static ReviewIssue InReview() => Checking().ToReview(new ChecksOutcome.Passed(), Head, Now);

    [Fact]
    public void WhenTheEscalatedRevisionIsClaimed_RevisionInProgressKeepsIt()
    {
        // Act
        RevisionInProgressIssue inProgress = InProgress();

        // Assert
        inProgress.DemandEscalated.ShouldBeTrue();
    }

    [Fact]
    public void WhenTheRevisionGoesToRebase_RebasePendingKeepsIt()
    {
        // Act
        RebasePendingIssue rebasing = Rebasing();

        // Assert
        rebasing.DemandEscalated.ShouldBeTrue();
    }

    [Fact]
    public void WhenARebaseConflictQueuesARevision_TheRevisionKeepsIt()
    {
        // Act
        RevisionQueuedIssue conflict = Rebasing().ResolveConflict(Now);

        // Assert
        conflict.DemandEscalated.ShouldBeTrue();
    }

    [Fact]
    public void WhenTheRebaseGoesToChecks_ChecksPendingKeepsIt()
    {
        // Act
        ChecksPendingIssue checking = Checking();

        // Assert
        checking.DemandEscalated.ShouldBeTrue();
    }

    [Fact]
    public void WhenChecksQueueAReview_ReviewQueuedAndReviewInProgressKeepIt()
    {
        // Act
        ReviewQueuedIssue queued = (ReviewQueuedIssue)Checking().ResolveVerdict(
            new ChecksOutcome.Passed(),
            autoMerge: false,
            reviewPullRequests: true,
            Head,
            Now);
        ReviewInProgressIssue reviewing = queued.Claim(WorkerRunId.New(), Now);

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.DemandEscalated.ShouldBeTrue(),
            () => reviewing.DemandEscalated.ShouldBeTrue());
    }

    [Fact]
    public void WhenChecksGoToReview_ReviewKeepsIt()
    {
        // Act
        ReviewIssue review = InReview();

        // Assert
        review.DemandEscalated.ShouldBeTrue();
    }

    [Fact]
    public void WhenChecksGoToMerge_MergePendingKeepsIt()
    {
        // Act
        MergePendingIssue merging = (MergePendingIssue)Checking().ResolveVerdict(
            new ChecksOutcome.Passed(),
            autoMerge: true,
            reviewPullRequests: false,
            Head,
            Now);

        // Assert
        merging.DemandEscalated.ShouldBeTrue();
    }

    [Fact]
    public void WhenTheRevisionRunFailsAndIsRetried_RevisionFailedAndTheRetryKeepIt()
    {
        // Act
        RevisionFailedIssue failed = InProgress().MarkFailed("Container exited with code 1", FailureCategory.NonZeroExit, Now);
        RevisionQueuedIssue retried = failed.Retry(Now);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.DemandEscalated.ShouldBeTrue(),
            () => retried.DemandEscalated.ShouldBeTrue());
    }

    [Fact]
    public void WhenTheRevisionAsksAndIsAnswered_AwaitingInputAndTheResumedRevisionKeepIt()
    {
        // Arrange
        AwaitingInputIssue awaiting = InProgress().MarkAwaitingInput(IssueBuilder.DefaultQuestions, Now, Now);
        awaiting.RecordQuestionPosted(1, Now, null);

        // Act
        RevisionQueuedIssue resumed = awaiting
            .ProvideInput(ClarificationInput.FromComments("Use Postgres."), Now.AddHours(1), Now.AddHours(1))
            .ShouldBeOfType<RevisionQueuedIssue>();

        // Assert
        awaiting.ShouldSatisfyAllConditions(
            () => awaiting.DemandEscalated.ShouldBeTrue(),
            () => resumed.DemandEscalated.ShouldBeTrue());
    }

    [Fact]
    public void WhenTheIssueFailsInReviewAndIsRetried_TheContinuationAndTheFreshRunKeepIt()
    {
        // Arrange
        ContinuableFailedIssue failed = InReview().Fail("Pull request closed.", FailureCategory.NonZeroExit, Now);

        // Act
        ContinuationQueuedIssue continuation = failed.Retry(Now);
        FreshQueuedIssue fresh = InReview().Fail("Pull request closed.", FailureCategory.NonZeroExit, Now).RetryFresh(Now);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.DemandEscalated.ShouldBeTrue(),
            () => continuation.DemandEscalated.ShouldBeTrue(),
            () => fresh.DemandEscalated.ShouldBeTrue());
    }

    [Fact]
    public void WhenTheSpecIsRewrittenAndTriageSaysRoutine_TheIssueStaysEscalatedAndDemanding()
    {
        // Arrange
        TriageQueuedIssue triageQueued = InReview().RetriageRewrittenSpec(Now);
        TriageInProgressIssue triaging = triageQueued.Claim(WorkerRunId.New(), Now);

        // Act
        RevisionQueuedIssue resumed = triaging
            .MarkReady(new TriageVerdict.Ready(IssueKind.Feature, Now, ImplementDemand.Routine, "none"), [], Now)
            .ShouldBeOfType<RevisionQueuedIssue>();

        // Assert
        resumed.ShouldSatisfyAllConditions(
            () => triageQueued.DemandEscalated.ShouldBeTrue(),
            () => triaging.DemandEscalated.ShouldBeTrue(),
            () => resumed.DemandEscalated.ShouldBeTrue(),
            () => resumed.ImplementDemand.ShouldBe(ImplementDemand.Demanding));
    }

    [Fact]
    public void WhenRetriageAsksAQuestion_AwaitingInputAndTriageBlockedKeepIt()
    {
        // Arrange
        TriageInProgressIssue triaging = InReview().RetriageRewrittenSpec(Now).Claim(WorkerRunId.New(), Now);

        // Act
        AwaitingInputIssue awaiting = triaging.MarkNeedsInput(
            new TriageVerdict.NeedsInput(IssueKind.Feature, IssueBuilder.DefaultQuestions, Now),
            [],
            Now);
        TriageBlockedIssue blocked = InReview().RetriageRewrittenSpec(Now).Block([3], Now);

        // Assert
        awaiting.ShouldSatisfyAllConditions(
            () => awaiting.DemandEscalated.ShouldBeTrue(),
            () => blocked.DemandEscalated.ShouldBeTrue());
    }

    [Fact]
    public void WhenRetriageFails_TriageFailedKeepsIt()
    {
        // Act
        TriageFailedIssue failed = InReview()
            .RetriageRewrittenSpec(Now)
            .Claim(WorkerRunId.New(), Now)
            .MarkFailed("boom", FailureCategory.VerdictInvalid, Now);

        // Assert
        failed.DemandEscalated.ShouldBeTrue();
    }
}
