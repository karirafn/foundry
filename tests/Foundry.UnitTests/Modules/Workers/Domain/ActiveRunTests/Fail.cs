using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Events;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ActiveRunTests;

public sealed class Fail
{
    private static ActiveRun CreateActiveRun(IssueId? issueId = null)
    {
        StartingRun starting = StartingRun.Begin(issueId ?? IssueId.New(), WorkerRunId.New());
        return starting.Activate(ContainerId.From("container-123"));
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
        ActiveRun active = CreateActiveRun(issueId);
        FailureReason reason = new FailureReason.TimedOut();

        // Act
        active.Fail(reason);

        // Assert
        WorkerRunFailed domainEvent = active.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<WorkerRunFailed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.WorkerRunId.ShouldBe(active.Id),
            () => domainEvent.IssueId.ShouldBe(issueId),
            () => domainEvent.ReasonDescription.ShouldBe(reason.ToString()),
            () => domainEvent.BranchName.ShouldBeNull(),
            () => domainEvent.LatestProgress.ShouldBeNull());
    }

    [Fact]
    public void WhenActiveRunHasBranchNameAndProgress_DomainEventIncludesThoseValues()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ActiveRun active = CreateActiveRun(issueId);
        active.SetBranchName(BranchName.From("feat/102-some-work"));
        active.UpdateProgress("Half done");
        FailureReason reason = new FailureReason.TimedOut();

        // Act
        active.Fail(reason);

        // Assert
        WorkerRunFailed domainEvent = active.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<WorkerRunFailed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.BranchName.ShouldBe("feat/102-some-work"),
            () => domainEvent.LatestProgress.ShouldBe("Half done"));
    }

    [Fact]
    public void WhenContainerOutputProvided_FailedRunPreservesContainerOutput()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        FailureReason reason = new FailureReason.NonZeroExit(1);
        string containerOutput = "Fatal error: process killed";

        // Act
        FailedRun failed = active.Fail(reason, containerOutput);

        // Assert
        failed.ContainerOutput.ShouldBe(containerOutput);
    }
}
