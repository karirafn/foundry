using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.WorkerRunTests;

public sealed class WorkerRunBaseTests
{
    private sealed class ConcreteWorkerRun : WorkerRun
    {
        public ConcreteWorkerRun(WorkerRunId id, IssueId issueId, DateTimeOffset createdAt)
            : base(id, issueId, createdAt)
        {
        }
    }

    [Fact]
    public void WhenCreated_IssueIdIsPreserved()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();
        IssueId issueId = IssueId.New();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;

        // Act
        ConcreteWorkerRun run = new(runId, issueId, createdAt);

        // Assert
        run.IssueId.ShouldBe(issueId);
    }

    [Fact]
    public void WhenCreated_CreatedAtIsPreserved()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();
        IssueId issueId = IssueId.New();
        DateTimeOffset createdAt = new(2026, 5, 28, 12, 0, 0, TimeSpan.Zero);

        // Act
        ConcreteWorkerRun run = new(runId, issueId, createdAt);

        // Assert
        run.CreatedAt.ShouldBe(createdAt);
    }

    [Fact]
    public void WhenCreated_IdIsPreserved()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();
        IssueId issueId = IssueId.New();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;

        // Act
        ConcreteWorkerRun run = new(runId, issueId, createdAt);

        // Assert
        run.Id.ShouldBe(runId);
    }

    [Fact]
    public void WhenCreated_ImplementsStateMachine()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();
        IssueId issueId = IssueId.New();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;

        // Act
        ConcreteWorkerRun run = new(runId, issueId, createdAt);

        // Assert
        run.ShouldBeAssignableTo<IStateMachine<WorkerRun>>();
    }
}
