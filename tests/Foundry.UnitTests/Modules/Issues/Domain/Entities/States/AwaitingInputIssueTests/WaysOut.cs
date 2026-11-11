using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.AwaitingInputIssueTests;

public sealed class WaysOut
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Complete_CarriesThePullRequestOfARevisionTarget()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder()
            .WithBranchName("feat/1-test")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/7")
            .AwaitingInput();

        // Act
        CompletedIssue completed = awaiting.Complete(Now);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.BranchName.ShouldBe("feat/1-test"),
            () => completed.LinkedPullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/7"),
            () => completed.CompletedAt.ShouldBe(Now));
    }

    [Fact]
    public void Fail_KeepsTheBranchContinuable()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().WithBranchName("feat/1-test").AwaitingInput();

        // Act
        ContinuableFailedIssue failed = awaiting.Fail("Pull request closed without merge", FailureCategory.PrClosed, Now);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.BranchName.ShouldBe("feat/1-test"),
            () => failed.FailureCategory.ShouldBe(FailureCategory.PrClosed),
            () => failed.Clarifications.ShouldBe(awaiting.Clarifications));
    }

    [Fact]
    public void Fail_WithoutAPullRequest_Throws()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputWithoutPullRequest(branchHasCommits: true);

        // Act + Assert
        Should.Throw<InvalidOperationException>(() => awaiting.Fail("closed", FailureCategory.PrClosed, Now));
    }
}
