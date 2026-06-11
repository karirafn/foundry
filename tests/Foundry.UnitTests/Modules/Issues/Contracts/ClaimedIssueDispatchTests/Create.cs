using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

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
            AccountToken: "ghp_test_token");

        // Assert
        dispatch.ShouldSatisfyAllConditions(
            () => dispatch.IssueId.ShouldBe(issueId),
            () => dispatch.IssueNumber.ShouldBe(42),
            () => dispatch.RepositorySlug.ShouldBe("org/repo"),
            () => dispatch.Revision.ShouldBeNull());
    }

    [Fact]
    public void WhenCreatedWithRevision_HoldsRevisionContext()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        RevisionContext revision = new(
            BranchName: "foundry/42",
            PullRequestUrl: "https://github.com/org/repo/pull/7",
            Comments: [new ReviewComment("Please fix this")]);

        // Act
        ClaimedIssueDispatch dispatch = new(
            issueId,
            WorkerRunId: Guid.NewGuid(),
            IssueNumber: 42,
            Title: "Fix the bug",
            Body: "Bug details",
            RepositorySlug: "org/repo",
            CloneUrl: new Uri("https://github.com/org/repo.git"),
            AccountToken: "ghp_test_token",
            Revision: revision);

        // Assert
        dispatch.Revision.ShouldBe(revision);
    }
}
