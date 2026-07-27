using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Domain.Events;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.StartingRunTests;

public sealed class Fail
{
    [Fact]
    public void WhenCalled_ReturnsFailedRunWithSameId()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        FailureReason reason = new FailureReason.ContainerError("image not found");

        // Act
        FailedRun failed = starting.Fail(reason);

        // Assert
        failed.Id.ShouldBe(starting.Id);
    }

    [Fact]
    public void WhenCalled_CopiesSharedProperties()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        FailureReason reason = new FailureReason.ContainerError("image not found");

        // Act
        FailedRun failed = starting.Fail(reason);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.IssueId.ShouldBe(issueId),
            () => failed.CreatedAt.ShouldBe(starting.CreatedAt));
    }

    [Fact]
    public void WhenCalled_SetsReason()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        FailureReason reason = new FailureReason.ContainerError("image not found");

        // Act
        FailedRun failed = starting.Fail(reason);

        // Assert
        failed.Reason.ShouldBe(reason);
    }

    [Fact]
    public void WhenCalled_SetsFailedAtToUtcNow()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        FailureReason reason = new FailureReason.ContainerError("image not found");
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        FailedRun failed = starting.Fail(reason);

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        failed.FailedAt.ShouldBeInRange(before, after);
    }

    [Fact]
    public void WhenCalled_RaisesWorkerRunFailedOnStartingRun()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        FailureReason reason = new FailureReason.ContainerError("image not found");

        // Act
        starting.Fail(reason);

        // Assert
        WorkerRunFailed domainEvent = starting.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<WorkerRunFailed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.WorkerRunId.ShouldBe(starting.Id),
            () => domainEvent.IssueId.ShouldBe(issueId),
            () => domainEvent.ReasonDescription.ShouldBe(reason.Summary),
            () => domainEvent.Category.ShouldBe(reason.CategoryToken),
            () => domainEvent.BranchName.ShouldBeNull());
    }
}
