using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ChecksPendingIssueTests;

public sealed class ResolveVerdict
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly IReadOnlyList<FailingCheck> Failing =
    [
        new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1"),
    ];

    [Fact]
    public void WhenPassedAndAutoMerge_ReturnsMergePendingPinnedToHead()
    {
        // Arrange
        RebaseOutcome rebaseOutcome = new RebaseOutcome.Rebased();
        ChecksPendingIssue checksPending = new IssueBuilder().WithRebaseOutcome(rebaseOutcome).ChecksPending();
        DateTimeOffset now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, Head, now);

        // Assert
        MergePendingIssue mergePending = next.ShouldBeOfType<MergePendingIssue>();
        mergePending.ShouldSatisfyAllConditions(
            () => mergePending.Id.ShouldBe(checksPending.Id),
            () => mergePending.HeadSha.ShouldBe(Head),
            () => mergePending.WorkerRunId.ShouldBe(checksPending.WorkerRunId),
            () => mergePending.BranchName.ShouldBe(checksPending.BranchName),
            () => mergePending.PullRequestUrl.ShouldBe(checksPending.PullRequestUrl),
            () => mergePending.FeedbackCutoffAt.ShouldBe(checksPending.FeedbackCutoffAt),
            () => mergePending.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>(),
            () => mergePending.RebaseOutcome.ShouldBe(rebaseOutcome),
            () => mergePending.EnteredMergePendingAt.ShouldBe(now),
            () => mergePending.CodeStep.ShouldBe(IssueCodeStep.Merging),
            () => mergePending.NeedsAction.ShouldBeFalse());
    }

    [Fact]
    public void WhenPassedAndAutoMerge_RaisesIssueMergePending()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().WithIssueNumber(7).ChecksPending();
        DateTimeOffset now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        // Act
        checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, Head, now);

        // Assert
        IssueMergePending raised = checksPending.DomainEvents.OfType<IssueMergePending>().ShouldHaveSingleItem();
        raised.ShouldSatisfyAllConditions(
            () => raised.IssueNumber.ShouldBe(7),
            () => raised.EnteredMergePendingAt.ShouldBe(now));
    }

    [Fact]
    public void WhenPassedWithoutAutoMerge_ReturnsReview()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, Head, DateTimeOffset.UtcNow);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.VerifiedHeadSha.ShouldBe(Head);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void WhenPassedAndAutoMergeWithoutHead_ReturnsReview(string? head)
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, head, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>();
    }

    public static TheoryData<ChecksOutcome> NotPassedOutcomes() =>
    [
        new ChecksOutcome.NoneDetected(),
        new ChecksOutcome.Unfinished(),
        new ChecksOutcome.Unreadable(),
        new ChecksOutcome.StillFailing([]),
    ];

    [Theory]
    [MemberData(nameof(NotPassedOutcomes))]
    public void WhenNotPassedAndAutoMerge_ReturnsReviewWithTheOutcome(ChecksOutcome outcome)
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(outcome, autoMerge: true, Head, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome.ShouldBe(outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhenStillFailingUnderCap_ReturnsChecksFixRevision(bool autoMerge)
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.StillFailing(Failing), autoMerge, Head, DateTimeOffset.UtcNow);

        // Assert
        RevisionQueuedIssue revision = next.ShouldBeOfType<RevisionQueuedIssue>();
        revision.Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>().FailingChecks.ShouldBe(Failing);
    }

    [Fact]
    public void WhenStillFailingAtCap_ReturnsReview()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().WithFailingChecks(Failing).ChecksPendingAtCap();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.StillFailing(Failing), autoMerge: true, Head, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome.ShouldBeOfType<ChecksOutcome.StillFailing>();
    }
}
