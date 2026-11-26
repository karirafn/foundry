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

/// <summary>
/// A checks verdict a person could merge as it stands rebases before review (ADR 0128); every other way into review
/// stays direct.
/// </summary>
public sealed class BeforeReadyRouting
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenPassedWithMergeAndReviewOff_RebasesBeforeReadyCarryingTheVerdictAndHead()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, reviewPullRequests: false, Head, Now);

        // Assert
        RebasePendingIssue rebasePending = next.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBe(new RebasePendingReason.BeforeReady(CheckOnly: false)),
            () => rebasePending.ChecksOutcome.ShouldBe(new ChecksOutcome.Passed()),
            () => rebasePending.VerifiedHeadSha.ShouldBe(Head),
            () => rebasePending.ReviewOutcome.ShouldBeNull(),
            () => rebasePending.BranchName.ShouldBe(checksPending.BranchName),
            () => rebasePending.PullRequestUrl.ShouldBe(checksPending.PullRequestUrl),
            () => rebasePending.EnteredRebasePendingAt.ShouldBe(Now),
            () => checksPending.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRebasePending>());
    }

    [Fact]
    public void WhenNoneDetectedWithMergeOn_RebasesBeforeReadyBecauseItNeverMergesItself()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.NoneDetected(), autoMerge: true, reviewPullRequests: false, Head, Now);

        // Assert
        RebasePendingIssue rebasePending = next.ShouldBeOfType<RebasePendingIssue>();
        rebasePending.ShouldSatisfyAllConditions(
            () => rebasePending.Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>(),
            () => rebasePending.ChecksOutcome.ShouldBe(new ChecksOutcome.NoneDetected()));
    }

    [Fact]
    public void WhenPassedWithMergeOnAfterPersonFeedback_RebasesBeforeReady()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithReviewComments([new ReviewComment("Please rename this.")])
            .ChecksPendingFromRevision();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, reviewPullRequests: false, Head, Now);

        // Assert
        next.ShouldBeOfType<RebasePendingIssue>().Reason.ShouldBeOfType<RebasePendingReason.BeforeReady>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenPassedWithAnUnknownHead_GoesToReviewDirectly(string? headSha)
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, reviewPullRequests: false, headSha, Now);

        // Assert
        next.ShouldBeOfType<ReviewIssue>();
    }

    [Fact]
    public void WhenRed_GoesToReviewDirectly()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPendingAtCap();
        ChecksOutcome red = new ChecksOutcome.StillFailing([new FailingCheck("ci/build", "FAILURE", null)]);

        // Act
        Issue next = checksPending.ResolveVerdict(red, autoMerge: false, reviewPullRequests: false, Head, Now);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome.ShouldBe(red);
    }

    [Fact]
    public void WhenUnfinished_GoesToReviewDirectly()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Unfinished(), autoMerge: false, reviewPullRequests: false, Head, Now);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome.ShouldBe(new ChecksOutcome.Unfinished());
    }

    [Fact]
    public void WhenUnreadable_GoesToReviewDirectly()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        Issue next = checksPending.ResolveVerdict(new ChecksOutcome.Unreadable(), autoMerge: false, reviewPullRequests: false, Head, Now);

        // Assert
        next.ShouldBeOfType<ReviewIssue>();
    }
}
