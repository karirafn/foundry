using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.WorkerRunCompletedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        Guid workerRunId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        string branchName = "foundry/42/add-feature";
        string pullRequestUrl = "https://github.com/owner/repo/pull/7";

        // Act
        WorkerRunCompleted @event = new(workerRunId, issueId, branchName, pullRequestUrl);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.ShouldSatisfyAllConditions(
            () => @event.WorkerRunId.ShouldBe(workerRunId),
            () => @event.IssueId.ShouldBe(issueId),
            () => @event.BranchName.ShouldBe(branchName),
            () => @event.PullRequestUrl.ShouldBe(pullRequestUrl));
    }

    [Fact]
    public void WhenCreatedWithNullBranchAndPullRequest_StoresNulls()
    {
        // Arrange
        Guid workerRunId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        // Act
        WorkerRunCompleted @event = new(workerRunId, issueId, null, null);

        // Assert
        @event.ShouldSatisfyAllConditions(
            () => @event.BranchName.ShouldBeNull(),
            () => @event.PullRequestUrl.ShouldBeNull());
    }
}
