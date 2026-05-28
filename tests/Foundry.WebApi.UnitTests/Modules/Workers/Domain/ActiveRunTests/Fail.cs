using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Domain.ActiveRunTests;

public sealed class Fail
{
    private static ActiveRun CreateActiveRun(IssueId? issueId = null)
    {
        StartingRun starting = StartingRun.Begin(issueId ?? IssueId.New());
        return starting.Activate("container-123");
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
            () => domainEvent.ReasonDescription.ShouldBe(reason.ToString()));
    }
}
