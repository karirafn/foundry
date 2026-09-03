using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.StartingRunTests;

public sealed class Activate
{
    [Fact]
    public void WhenCalled_ReturnsActiveRunWithSameId()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        // Act
        ActiveRun active = starting.Activate(ContainerId.From("container-123"), BranchName.From("feat/1-my-feature"), MonitoredRepositoryId.New());

        // Assert
        active.Id.ShouldBe(starting.Id);
    }

    [Fact]
    public void WhenCalled_CopiesSharedProperties()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());

        // Act
        ActiveRun active = starting.Activate(ContainerId.From("container-123"), BranchName.From("feat/1-my-feature"), MonitoredRepositoryId.New());

        // Assert
        active.ShouldSatisfyAllConditions(
            () => active.IssueId.ShouldBe(issueId),
            () => active.CreatedAt.ShouldBe(starting.CreatedAt));
    }

    [Fact]
    public void WhenCalled_SetsContainerId()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        // Act
        ActiveRun active = starting.Activate(ContainerId.From("container-abc"), BranchName.From("feat/1-my-feature"), MonitoredRepositoryId.New());

        // Assert
        active.ContainerId.ShouldBe(ContainerId.From("container-abc"));
    }

    [Fact]
    public void WhenCalled_SetsBranchName()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        // Act
        ActiveRun active = starting.Activate(ContainerId.From("container-123"), BranchName.From("feat/42-new-feature"), MonitoredRepositoryId.New());

        // Assert
        active.BranchName.ShouldBe(BranchName.From("feat/42-new-feature"));
    }

    [Fact]
    public void WhenCalled_SetsStartedAtToUtcNow()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        ActiveRun active = starting.Activate(ContainerId.From("container-123"), BranchName.From("feat/1-my-feature"), MonitoredRepositoryId.New());

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        active.StartedAt.ShouldBeInRange(before, after);
    }

    [Fact]
    public void WhenCalled_RaisesWorkerRunStartedOnStartingRun()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());

        // Act
        starting.Activate(ContainerId.From("container-123"), BranchName.From("feat/1-my-feature"), MonitoredRepositoryId.New());

        // Assert
        WorkerRunStarted domainEvent = starting.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<WorkerRunStarted>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.WorkerRunId.ShouldBe(starting.Id),
            () => domainEvent.IssueId.ShouldBe(issueId));
    }

    [Fact]
    public void WhenCalledWithRepositoryId_SetsMonitoredRepositoryIdOnActiveRun()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        ActiveRun active = starting.Activate(
            ContainerId.From("container-123"),
            BranchName.From("feat/1-my-feature"),
            repositoryId);

        // Assert
        active.MonitoredRepositoryId.ShouldBe(repositoryId);
    }
}
