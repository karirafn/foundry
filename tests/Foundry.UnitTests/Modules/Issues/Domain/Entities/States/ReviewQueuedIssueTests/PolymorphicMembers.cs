using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewQueuedIssueTests;

public sealed class PolymorphicMembers
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    [Fact]
    public void WhenQueued_IsInTheReviewStageAwaitingAReviewRun()
    {
        // Arrange
        ReviewQueuedIssue queued = new IssueBuilder().ReviewQueued();

        // Act & Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.Stage.ShouldBe(IssueStage.Review),
            () => queued.RunKind.ShouldBe(RunKind.Review),
            () => queued.AwaitsDispatch.ShouldBeTrue(),
            () => queued.NeedsAction.ShouldBeFalse(),
            () => queued.IsRestingState().ShouldBeFalse());
    }

    [Fact]
    public void WhenQueued_TierRankIsOneBehindRevisionsOnly()
    {
        // Arrange
        ReviewQueuedIssue queued = new IssueBuilder().ReviewQueued();

        // Act
        int tierRank = queued.TierRank;

        // Assert
        tierRank.ShouldBe(1);
    }

    [Fact]
    public void WhenQueued_ContextIsAReviewOfTheVerifiedHeadUnderTheReviewRole()
    {
        // Arrange
        ReviewQueuedIssue queued = new IssueBuilder()
            .WithBranchName("feat/7-retry")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/7")
            .WithVerifiedHeadSha(Head)
            .ReviewQueued();

        // Act
        DispatchContext context = queued.Context;

        // Assert
        context.ShouldSatisfyAllConditions(
            () => context.ShouldBe(new DispatchContext.Review("feat/7-retry", "https://github.com/owner/repo/pull/7", Head)),
            () => context.Role.ShouldBe(DispatchRole.Review),
            () => queued.DispatchBranchName.ShouldBeNull());
    }

    [Fact]
    public void WhenClaimed_ReturnsReviewInProgressWithTheReviewRun()
    {
        // Arrange
        ReviewQueuedIssue queued = new IssueBuilder().WithVerifiedHeadSha(Head).WithAutoMerge(true).ReviewQueued();
        WorkerRunId reviewRun = WorkerRunId.New();

        // Act
        ReviewInProgressIssue inProgress = queued.Claim(reviewRun, DateTimeOffset.UtcNow);

        // Assert
        inProgress.ShouldSatisfyAllConditions(
            () => inProgress.WorkerRunId.ShouldBe(reviewRun),
            () => inProgress.ReviewedCommit.ShouldBe(Head),
            () => inProgress.AutoMerge.ShouldBeTrue(),
            () => inProgress.Stage.ShouldBe(IssueStage.Review),
            () => inProgress.RunKind.ShouldBe(RunKind.Review),
            () => queued.DomainEvents.OfType<IssueReviewInProgress>().ShouldHaveSingleItem());
    }
}
