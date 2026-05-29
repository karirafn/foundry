using Foundry.Modules.Issues.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.ClaimedIssueDispatchTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_HoldsProvidedValues()
    {
        // Arrange
        IssueId issueId = IssueId.New();

        // Act
        ClaimedIssueDispatch dispatch = new(
            issueId,
            WorkerRunId: Guid.NewGuid(),
            IssueNumber: 42,
            Title: "Fix the bug",
            Body: "Bug details",
            RepositorySlug: "org/repo",
            CloneUrl: new Uri("https://github.com/org/repo.git"),
            AccountSecretKeyName: "github-pat");

        // Assert
        dispatch.ShouldSatisfyAllConditions(
            () => dispatch.IssueId.ShouldBe(issueId),
            () => dispatch.IssueNumber.ShouldBe(42),
            () => dispatch.RepositorySlug.ShouldBe("org/repo"));
    }
}
