using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.ActiveRunTests;

public sealed class RequestCancel
{
    private static ActiveRun CreateActiveRun()
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        return starting.Activate(
            ContainerId.From("container-123"),
            BranchName.From("feat/1-default"),
            MonitoredRepositoryId.New());
    }

    [Fact]
    public void WhenNotRequested_CancelRequestedAtIsNull()
    {
        // Arrange / Act
        ActiveRun active = CreateActiveRun();

        // Assert
        active.CancelRequestedAt.ShouldBeNull();
    }

    [Fact]
    public void WhenCalled_SetsCancelRequestedAt()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset requestedAt = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

        // Act
        active.RequestCancel(requestedAt);

        // Assert
        active.CancelRequestedAt.ShouldBe(requestedAt);
    }

    [Fact]
    public void WhenCalledTwice_KeepsFirstTimestamp()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset first = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset second = first.AddMinutes(1);
        active.RequestCancel(first);

        // Act
        active.RequestCancel(second);

        // Assert
        active.CancelRequestedAt.ShouldBe(first);
    }

    [Fact]
    public void WhenCalled_RaisesNoDomainEvent()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        active.ClearDomainEvents();

        // Act
        active.RequestCancel(DateTimeOffset.UtcNow);

        // Assert
        active.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenCompletedAfterCancelRequest_CompletedRunCarriesCancelRequestedAt()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset requestedAt = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
        active.RequestCancel(requestedAt);

        // Act
        CompletedRun completed = active.Complete(0, active.BranchName, PullRequestUrl.From("https://github.com/o/r/pull/1"));

        // Assert
        completed.CancelRequestedAt.ShouldBe(requestedAt);
    }

    [Fact]
    public void WhenFailedAfterCancelRequest_FailedRunCarriesCancelRequestedAt()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset requestedAt = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
        active.RequestCancel(requestedAt);

        // Act
        FailedRun failed = active.Fail(new FailureReason.Cancelled(), active.BranchName);

        // Assert
        failed.CancelRequestedAt.ShouldBe(requestedAt);
    }

    [Theory]
    [InlineData(typeof(StartingRun))]
    [InlineData(typeof(CompletedRun))]
    [InlineData(typeof(FailedRun))]
    public void NonActiveStates_ExposeNoRequestCancel(Type stateType)
    {
        // Act
        System.Reflection.MethodInfo? method = stateType.GetMethod(nameof(ActiveRun.RequestCancel));

        // Assert
        method.ShouldBeNull();
    }
}
