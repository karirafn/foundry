using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.IssueClaimedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ClaimedIssueDispatch dispatch = new(
            issueId,
            WorkerRunId: Guid.NewGuid(),
            IssueNumber: 42,
            Title: "Fix the bug",
            Body: "Bug details",
            RepositorySlug: "org/repo",
            CloneUrl: new Uri("https://github.com/org/repo.git"),
            AccountToken: "ghp_test_token",
            BranchName: "feat/42",
            MonitoredRepositoryId: repositoryId,
            Provider: new WorkerProvider.GitHub());

        // Act
        IssueClaimed @event = new(dispatch);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.Dispatch.IssueId.ShouldBe(issueId);
    }
}
