using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

using WorkerRunId = Foundry.Modules.Workers.Contracts.WorkerRunId;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.ActiveRunTests;

public sealed class Fail
{
    private static ActiveRun CreateActiveRun(IssueId? issueId = null, BranchName? branchName = null)
    {
        StartingRun starting = StartingRun.Begin(issueId ?? IssueId.New(), WorkerRunId.New());
        return starting.Activate(ContainerId.From("container-123"), branchName ?? BranchName.From("feat/1-default"), MonitoredRepositoryId.New());
    }

    [Fact]
    public void WhenCalled_ReturnsFailedRunWithSameId()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        FailureReason reason = new FailureReason.NonZeroExit(1);

        // Act
        FailedRun failed = active.Fail(reason, branchNameOrNull: null);

        // Assert
        failed.Id.ShouldBe(active.Id);
    }

    [Fact]
    public void WhenCalled_CopiesSharedProperties()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ActiveRun active = CreateActiveRun(issueId);
        FailureReason reason = new FailureReason.NonZeroExit(1);

        // Act
        FailedRun failed = active.Fail(reason, branchNameOrNull: null);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.IssueId.ShouldBe(issueId),
            () => failed.CreatedAt.ShouldBe(active.CreatedAt));
    }

    [Fact]
    public void WhenCalled_SetsReason()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        FailureReason reason = new FailureReason.TimedOut();

        // Act
        FailedRun failed = active.Fail(reason, branchNameOrNull: null);

        // Assert
        failed.Reason.ShouldBe(reason);
    }

    [Fact]
    public void WhenCalled_SetsFailedAtToUtcNow()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        FailureReason reason = new FailureReason.NonZeroExit(2);
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        FailedRun failed = active.Fail(reason, branchNameOrNull: null);

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        failed.FailedAt.ShouldBeInRange(before, after);
    }

    [Fact]
    public void WhenCalledWithBranchName_RaisesWorkerRunFailedWithBranchName()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        BranchName branchName = BranchName.From("feat/102-some-work");
        ActiveRun active = CreateActiveRun(issueId, branchName);
        FailureReason reason = new FailureReason.TimedOut();

        // Act
        active.Fail(reason, branchNameOrNull: BranchName.From("feat/102-some-work"));

        // Assert
        WorkerRunFailed domainEvent = active.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<WorkerRunFailed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.WorkerRunId.ShouldBe(active.Id),
            () => domainEvent.IssueId.ShouldBe(issueId),
            () => domainEvent.ReasonDescription.ShouldBe(reason.Summary),
            () => domainEvent.Category.ShouldBe(reason.CategoryToken),
            () => domainEvent.BranchName.ShouldBe("feat/102-some-work"));
    }

    [Fact]
    public void WhenCalledWithNullBranchName_RaisesWorkerRunFailedWithNullBranchName()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        BranchName branchName = BranchName.From("feat/103-terminal-work");
        ActiveRun active = CreateActiveRun(issueId, branchName);
        FailureReason reason = new FailureReason.NonZeroExit(1);

        // Act
        active.Fail(reason, branchNameOrNull: null);

        // Assert
        WorkerRunFailed domainEvent = active.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<WorkerRunFailed>();
        domainEvent.BranchName.ShouldBeNull();
    }

}
