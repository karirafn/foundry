using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewFailedIssueTests;

public sealed class Retry
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    [Fact]
    public void WhenRetried_QueuesTheReviewOfTheSameCommitAgain()
    {
        // Arrange
        ReviewFailedIssue failed = new IssueBuilder().WithVerifiedHeadSha(Head).WithAutoMerge(true).ReviewFailed();

        // Act
        ReviewQueuedIssue queued = failed.Retry(DateTimeOffset.UtcNow);

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.ReviewedCommit.ShouldBe(Head),
            () => queued.AutoMerge.ShouldBeTrue(),
            () => queued.BranchName.ShouldBe(failed.BranchName),
            () => queued.PullRequestUrl.ShouldBe(failed.PullRequestUrl),
            () => failed.DomainEvents.OfType<IssueReviewQueued>().ShouldHaveSingleItem());
    }
}
