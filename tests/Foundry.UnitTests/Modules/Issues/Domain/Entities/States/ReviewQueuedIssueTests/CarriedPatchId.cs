using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewQueuedIssueTests;

public sealed class CarriedPatchId
{
    private const string ReviewedHead = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";
    private const string RebasedHead = "9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b3a291807";
    private const string PatchId = "0123456789012345678901234567890123456789";

    private static readonly RevisionTrigger.ReviewFindings NoFindings =
        new([], [], ReviewTestsAssessment.Adequate, "Covered.");

    [Fact]
    public void WhenAnApprovedIssueIsCaughtUpAndQueuedForReviewAgain_TheContextCarriesTheApprovalsPatchId()
    {
        // Arrange
        WorkerRunId reviewRun = WorkerRunId.New();
        ReviewInProgressIssue reviewing = new IssueBuilder()
            .WithWorkerRunId(reviewRun)
            .WithVerifiedHeadSha(ReviewedHead)
            .WithAutoMerge(true)
            .ReviewInProgress();
        MergePendingIssue approved = reviewing
            .ResolveVerdict(ReviewVerdictOutcome.Approved, PatchId, NoFindings, DateTimeOffset.UtcNow)
            .ShouldBeOfType<MergePendingIssue>();
        ChecksPendingIssue rebased = approved
            .CatchUp(DateTimeOffset.UtcNow)
            .MarkChecksPending(new RebaseOutcome.Rebased(), DateTimeOffset.UtcNow);

        // Act
        ReviewQueuedIssue queued = rebased
            .ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, reviewPullRequests: true, RebasedHead, DateTimeOffset.UtcNow)
            .ShouldBeOfType<ReviewQueuedIssue>();

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.LatestReview.ShouldBe(new LatestReview(reviewRun, ReviewVerdictOutcome.Approved, PatchId)),
            () => queued.Context.ShouldBe(new DispatchContext.Review(
                queued.BranchName,
                queued.PullRequestUrl,
                RebasedHead,
                CarriedPatchId: PatchId)));
    }

    [Fact]
    public void WhenTheLatestReviewApprovedForAPerson_TheContextCarriesItsPatchId()
    {
        // Arrange
        ReviewQueuedIssue queued = QueuedAgainAfter(ReviewVerdictOutcome.ApprovedForPerson, PatchId);

        // Act
        DispatchContext context = queued.Context;

        // Assert
        context.ShouldBeOfType<DispatchContext.Review>().CarriedPatchId.ShouldBe(PatchId);
    }

    [Fact]
    public void WhenTheLatestReviewRequestedChanges_TheContextCarriesNoPatchId()
    {
        // Arrange
        ReviewQueuedIssue queued = QueuedAgainAfter(ReviewVerdictOutcome.ChangesRequested, PatchId);

        // Act
        DispatchContext context = queued.Context;

        // Assert
        context.ShouldBeOfType<DispatchContext.Review>().CarriedPatchId.ShouldBeNull();
    }

    [Fact]
    public void WhenTheLatestApprovalHadNoPatchId_TheContextCarriesNone()
    {
        // Arrange
        ReviewQueuedIssue queued = QueuedAgainAfter(ReviewVerdictOutcome.Approved, patchId: null);

        // Act
        DispatchContext context = queued.Context;

        // Assert
        context.ShouldBeOfType<DispatchContext.Review>().CarriedPatchId.ShouldBeNull();
    }

    [Fact]
    public void WhenTheIssueWasNeverReviewed_TheContextCarriesNoPatchId()
    {
        // Arrange
        ReviewQueuedIssue queued = new IssueBuilder().ReviewQueued();

        // Act
        DispatchContext context = queued.Context;

        // Assert
        context.ShouldBeOfType<DispatchContext.Review>().CarriedPatchId.ShouldBeNull();
    }

    // An approval routes to a person and a push in review sends it back through the checks gate; a request for changes
    // sends it back through a findings fix.
    private static ReviewQueuedIssue QueuedAgainAfter(ReviewVerdictOutcome outcome, string? patchId)
    {
        ReviewInProgressIssue reviewing = new IssueBuilder()
            .WithVerifiedHeadSha(ReviewedHead)
            .WithAutoMerge(false)
            .ReviewInProgress();
        Issue reviewed = reviewing.ResolveVerdict(outcome, patchId, NoFindings, DateTimeOffset.UtcNow);

        ChecksPendingIssue checksPending = reviewed switch
        {
            ReviewIssue review => review.RecheckAfterHeadMoved(DateTimeOffset.UtcNow),
            RevisionQueuedIssue findingsFix => findingsFix
                .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow)
                .MarkRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                .MarkChecksPending(new RebaseOutcome.UpToDate(), DateTimeOffset.UtcNow),
            _ => throw new InvalidOperationException($"Unexpected state after review: {reviewed.GetType().Name}"),
        };

        return checksPending
            .ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, reviewPullRequests: true, RebasedHead, DateTimeOffset.UtcNow)
            .ShouldBeOfType<ReviewQueuedIssue>();
    }
}
