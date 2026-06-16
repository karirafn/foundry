using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ActiveRunTests;

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
        FailedRun failed = active.Fail(reason);

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
        FailedRun failed = active.Fail(reason);

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
        FailedRun failed = active.Fail(reason);

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
        FailedRun failed = active.Fail(reason);

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        failed.FailedAt.ShouldBeInRange(before, after);
    }

    [Fact]
    public void WhenCalled_RaisesWorkerRunFailedOnActiveRun()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        BranchName branchName = BranchName.From("feat/102-some-work");
        ActiveRun active = CreateActiveRun(issueId, branchName);
        FailureReason reason = new FailureReason.TimedOut();

        // Act
        active.Fail(reason);

        // Assert
        WorkerRunFailed domainEvent = active.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<WorkerRunFailed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.WorkerRunId.ShouldBe(active.Id),
            () => domainEvent.IssueId.ShouldBe(issueId),
            () => domainEvent.ReasonDescription.ShouldBe(reason.ToString()),
            () => domainEvent.BranchName.ShouldBe("feat/102-some-work"));
    }

}
