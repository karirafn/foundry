using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.StartingRunTests;

public sealed class Begin
{
    [Fact]
    public void WhenCalled_ReturnsStartingRunWithCorrectIssueId()
    {
        // Arrange
        IssueId issueId = IssueId.New();

        // Act
        StartingRun run = StartingRun.Begin(issueId, WorkerRunId.New());

        // Assert
        run.IssueId.ShouldBe(issueId);
    }

    [Fact]
    public void WhenCalled_UsesProvidedWorkerRunId()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        WorkerRunId workerRunId = WorkerRunId.New();

        // Act
        StartingRun run = StartingRun.Begin(issueId, workerRunId);

        // Assert
        run.Id.ShouldBe(workerRunId);
    }

    [Fact]
    public void WhenCalled_SetsCreatedAtToUtcNow()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        StartingRun run = StartingRun.Begin(issueId, WorkerRunId.New());

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        run.CreatedAt.ShouldBeInRange(before, after);
    }
}
