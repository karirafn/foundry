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
        WorkerRunId workerRunId = WorkerRunId.New();
        Guid issueId = Guid.NewGuid();
        string branchName = "foundry/42/add-feature";
        string pullRequestUrl = "https://github.com/owner/repo/pull/7";

        // Act
        WorkerRunCompleted @event = new(workerRunId, issueId, branchName, pullRequestUrl, WorkerRunMergeState.Open);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.ShouldSatisfyAllConditions(
            () => @event.WorkerRunId.ShouldBe(workerRunId),
            () => @event.IssueId.ShouldBe(issueId),
            () => @event.BranchName.ShouldBe(branchName),
            () => @event.PullRequestUrl.ShouldBe(pullRequestUrl),
            () => @event.MergeState.ShouldBe(WorkerRunMergeState.Open));
    }

    [Fact]
    public void WhenCreatedWithNullBranchAndPullRequest_StoresNulls()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        Guid issueId = Guid.NewGuid();

        // Act
        WorkerRunCompleted @event = new(workerRunId, issueId, null, null, WorkerRunMergeState.None);

        // Assert
        @event.ShouldSatisfyAllConditions(
            () => @event.BranchName.ShouldBeNull(),
            () => @event.PullRequestUrl.ShouldBeNull(),
            () => @event.MergeState.ShouldBe(WorkerRunMergeState.None));
    }

    [Fact]
    public void WhenCreatedWithRunStartedAt_StoresValue()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        Guid issueId = Guid.NewGuid();
        DateTimeOffset runStartedAt = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);

        // Act
        WorkerRunCompleted @event = new(
            workerRunId,
            issueId,
            BranchName: "feat/42-fix",
            PullRequestUrl: "https://github.com/owner/repo/pull/42",
            MergeState: WorkerRunMergeState.Open,
            RunStartedAt: runStartedAt);

        // Assert
        @event.RunStartedAt.ShouldBe(runStartedAt);
    }

    [Fact]
    public void WhenCreatedWithoutRunStartedAt_IsNull()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        Guid issueId = Guid.NewGuid();

        // Act
        WorkerRunCompleted @event = new(workerRunId, issueId, null, null, WorkerRunMergeState.None);

        // Assert
        @event.RunStartedAt.ShouldBeNull();
    }
}
