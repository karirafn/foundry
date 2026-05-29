using Foundry.Modules.Issues.Contracts;
using Foundry.WebApi.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Domain.StartingRunTests;

public sealed class Activate
{
    [Fact]
    public void WhenCalled_ReturnsActiveRunWithSameId()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        // Act
        ActiveRun active = starting.Activate(ContainerId.From("container-123"));

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
        ActiveRun active = starting.Activate(ContainerId.From("container-123"));

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
        ActiveRun active = starting.Activate(ContainerId.From("container-abc"));

        // Assert
        active.ContainerId.ShouldBe(ContainerId.From("container-abc"));
    }

    [Fact]
    public void WhenCalled_SetsStartedAtToUtcNow()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        ActiveRun active = starting.Activate(ContainerId.From("container-123"));

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
        starting.Activate(ContainerId.From("container-123"));

        // Assert
        WorkerRunStarted domainEvent = starting.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<WorkerRunStarted>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.WorkerRunId.ShouldBe(starting.Id),
            () => domainEvent.IssueId.ShouldBe(issueId));
    }
}
