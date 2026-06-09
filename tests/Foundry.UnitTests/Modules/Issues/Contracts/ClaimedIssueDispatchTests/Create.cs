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
            AccountSecretKeyName: "github-pat");

        // Assert
        dispatch.ShouldSatisfyAllConditions(
            () => dispatch.IssueId.ShouldBe(issueId),
            () => dispatch.IssueNumber.ShouldBe(42),
            () => dispatch.RepositorySlug.ShouldBe("org/repo"),
            () => dispatch.Revision.ShouldBeNull(),
            () => dispatch.Continuation.ShouldBeNull());
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
            AccountSecretKeyName: "github-pat",
            Revision: revision);

        // Assert
        dispatch.Revision.ShouldBe(revision);
    }

    [Fact]
    public void WhenCreatedWithContinuation_HoldsContinuationContext()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ContinuationContext continuation = new(
            BranchName: "foundry/42/add-feature",
            LatestProgress: "Implemented the core feature");

        // Act
        ClaimedIssueDispatch dispatch = new(
            issueId,
            WorkerRunId: Guid.NewGuid(),
            IssueNumber: 42,
            Title: "Fix the bug",
            Body: "Bug details",
            RepositorySlug: "org/repo",
            CloneUrl: new Uri("https://github.com/org/repo.git"),
            AccountSecretKeyName: "github-pat",
            Continuation: continuation);

        // Assert
        dispatch.Continuation.ShouldBe(continuation);
    }
}
