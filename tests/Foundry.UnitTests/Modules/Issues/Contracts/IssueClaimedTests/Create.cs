using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
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
            WorkerRunId: WorkerRunId.New(),
            IssueNumber: 42,
            Title: "Fix the bug",
            RepositorySlug: "org/repo",
            CloneUrl: new Uri("https://github.com/org/repo.git"),
            AccountToken: "ghp_test_token",
            BranchName: BranchName.From("feat/42"),
            MonitoredRepositoryId: repositoryId,
            Provider: new WorkerProvider.GitHub(),
            Context: new DispatchContext.Fresh("feat/42"),
            IssueApiUrl: "https://api.github.com/repos/org/repo/issues/42");

        // Act
        IssueClaimed @event = new(dispatch);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.Dispatch.IssueId.ShouldBe(issueId);
    }
}
