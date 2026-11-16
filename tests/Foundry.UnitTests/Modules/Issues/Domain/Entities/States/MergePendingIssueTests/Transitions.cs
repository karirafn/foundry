using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.MergePendingIssueTests;

public sealed class Transitions
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    [Fact]
    public void Complete_ReturnsCompletedIssueCarryingBranchAndPullRequest()
    {
        // Arrange
        MergePendingIssue mergePending = new IssueBuilder()
            .WithBranchName("feat/1-merge")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/9")
            .MergePending();
        DateTimeOffset completedAt = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        // Act
        CompletedIssue completed = mergePending.Complete(completedAt);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.Id.ShouldBe(mergePending.Id),
            () => completed.BranchName.ShouldBe("feat/1-merge"),
            () => completed.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/9"),
            () => completed.CompletedAt.ShouldBe(completedAt));
        mergePending.DomainEvents.OfType<IssueCompleted>().ShouldHaveSingleItem();
    }

    public static TheoryData<MergeOutcome> NotMergedOutcomes() =>
    [
        new MergeOutcome.Disabled(),
        new MergeOutcome.Skipped(MergeSkipReasons.HeadMoved),
    ];

    [Theory]
    [MemberData(nameof(NotMergedOutcomes))]
    public void ToReview_CarriesChecksRebaseAndMergeOutcomes(MergeOutcome mergeOutcome)
    {
        // Arrange
        RebaseOutcome rebaseOutcome = new RebaseOutcome.Rebased();
        MergePendingIssue mergePending = new IssueBuilder()
            .WithRebaseOutcome(rebaseOutcome)
            .WithVerifiedHeadSha(Head)
            .MergePending();

        // Act
        ReviewIssue review = mergePending.ToReview(mergeOutcome, DateTimeOffset.UtcNow);

        // Assert
        review.ShouldSatisfyAllConditions(
            () => review.Id.ShouldBe(mergePending.Id),
            () => review.WorkerRunId.ShouldBe(mergePending.WorkerRunId),
            () => review.BranchName.ShouldBe(mergePending.BranchName),
            () => review.PullRequestUrl.ShouldBe(mergePending.PullRequestUrl),
            () => review.FeedbackCutoffAt.ShouldBe(mergePending.FeedbackCutoffAt),
            () => review.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>(),
            () => review.RebaseOutcome.ShouldBe(rebaseOutcome),
            () => review.MergeOutcome.ShouldBe(mergeOutcome),
            () => review.VerifiedHeadSha.ShouldBe(Head));
        mergePending.DomainEvents.OfType<IssueInReview>().ShouldHaveSingleItem();
    }

    [Fact]
    public void ToReview_WhenMerged_Throws()
    {
        // Arrange
        MergePendingIssue mergePending = new IssueBuilder().MergePending();

        // Act
        Action act = () => mergePending.ToReview(new MergeOutcome.Merged(), DateTimeOffset.UtcNow);

        // Assert
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Fail_ReturnsContinuableFailedIssueWithCategory()
    {
        // Arrange
        MergePendingIssue mergePending = new IssueBuilder().MergePending();

        // Act
        ContinuableFailedIssue failed = mergePending.Fail("Pull request closed without merge", FailureCategory.PrClosed, DateTimeOffset.UtcNow);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.Id.ShouldBe(mergePending.Id),
            () => failed.WorkerRunId.ShouldBe(mergePending.WorkerRunId),
            () => failed.BranchName.ShouldBe(mergePending.BranchName),
            () => failed.FailureCategory.ShouldBe(FailureCategory.PrClosed));
        mergePending.DomainEvents.OfType<IssueContinuableFailed>().ShouldHaveSingleItem();
    }

    [Fact]
    public void CatchUp_RebasesForTheCatchUpPinnedToTheVerifiedHead()
    {
        // Arrange
        MergePendingIssue mergePending = new IssueBuilder()
            .WithBranchName("feat/1-merge")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/9")
            .WithVerifiedHeadSha(Head)
            .MergePending();
        DateTimeOffset transitionedAt = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

        // Act
        RebasePendingIssue rebasePending = mergePending.CatchUp(transitionedAt);

        // Assert
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Id.ShouldBe(mergePending.Id),
            () => rebasePending.Reason.ShouldBe(new RebasePendingReason.CatchUp()),
            () => rebasePending.CatchUpHeadSha.ShouldBe(Head),
            () => rebasePending.BranchName.ShouldBe("feat/1-merge"),
            () => rebasePending.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/9"),
            () => rebasePending.WorkerRunId.ShouldBe(mergePending.WorkerRunId),
            () => rebasePending.FeedbackCutoffAt.ShouldBe(mergePending.FeedbackCutoffAt),
            () => rebasePending.RebaseConflictAttempts.ShouldBe(mergePending.RebaseConflictAttempts),
            () => rebasePending.EnteredRebasePendingAt.ShouldBe(transitionedAt));
        IssueRebasePending domainEvent = mergePending.DomainEvents.OfType<IssueRebasePending>().ShouldHaveSingleItem();
        domainEvent.EnteredRebasePendingAt.ShouldBe(transitionedAt);
    }
}
