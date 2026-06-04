using Foundry.Modules.Issues.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.IssueStateDetailsTests;

public sealed class Violations
{
    [Fact]
    public void WhenViolationsProvided_HoldsViolations()
    {
        // Arrange
        List<EligibilityViolationDto> violations =
        [
            new EligibilityViolationDto("branch-protection:allow-direct-pushes", "Direct pushes allowed.")
        ];

        // Act
        IssueStateDetails details = new(
            WorkerRunId: null,
            BranchName: null,
            PullRequestUrl: null,
            FeedbackCutoffAt: null,
            FailureReason: null,
            FailedAt: null,
            CompletedAt: null,
            BlockedBy: null,
            Violations: violations);

        // Assert
        details.Violations.ShouldNotBeNull();
        details.Violations!.Count.ShouldBe(1);
        details.Violations[0].Rule.ShouldBe("branch-protection:allow-direct-pushes");
    }

    [Fact]
    public void WhenViolationsOmitted_IsNull()
    {
        // Arrange / Act
        IssueStateDetails details = new(
            WorkerRunId: null,
            BranchName: null,
            PullRequestUrl: null,
            FeedbackCutoffAt: null,
            FailureReason: null,
            FailedAt: null,
            CompletedAt: null,
            BlockedBy: null,
            Violations: null);

        // Assert
        details.Violations.ShouldBeNull();
    }
}
