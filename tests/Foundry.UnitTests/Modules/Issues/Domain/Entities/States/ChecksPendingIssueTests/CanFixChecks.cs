using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ChecksPendingIssueTests;

public sealed class CanFixChecks
{
    private static readonly IReadOnlyList<FailingCheck> FailingChecks =
    [
        new FailingCheck("ci/build", "FAILURE", "https://github.com/owner/repo/actions/runs/1"),
    ];

    [Fact]
    public void WhenAttemptsRemainAndAFailingCheckIsNamed_ReturnsTrue()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .ChecksPending();

        // Act
        bool canFix = checksPending.CanFixChecks(FailingChecks);

        // Assert
        canFix.ShouldBeTrue();
    }

    [Fact]
    public void WhenNoFailingCheckIsNamed_ReturnsFalse()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .ChecksPending();

        // Act
        bool canFix = checksPending.CanFixChecks([]);

        // Assert
        canFix.ShouldBeFalse();
    }

    [Fact]
    public void WhenOneAttemptIsUsed_ReturnsTrue()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .ChecksPendingFromFixChecks();

        // Act
        bool canFix = checksPending.CanFixChecks(FailingChecks);

        // Assert
        canFix.ShouldBeTrue();
    }

    [Fact]
    public void WhenAtTheFixCap_ReturnsFalse()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .ChecksPendingAtCap();

        // Act
        bool canFix = checksPending.CanFixChecks(FailingChecks);

        // Assert
        canFix.ShouldBeFalse();
    }
}
