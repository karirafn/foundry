using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.StartingRunTests;

public sealed class RecordSuppliedOpenIssues
{
    private static StartingRun RunSupplied(TrackerContext trackerContext)
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        starting.RecordSuppliedOpenIssues(trackerContext);
        return starting;
    }

    private static TrackerContext MixedContext() => new TrackerContextBuilder()
        .WithReferencedIssue(7, isOpen: true)
        .WithReferencedIssue(3, isOpen: true)
        .WithOpenIssueNumbers(11, 12)
        .Build();

    private static ActiveRun ActivatedRun() =>
        RunSupplied(MixedContext())
            .Activate(ContainerId.From("container-1"), BranchName.From("feat/1-x"), MonitoredRepositoryId.New());

    [Fact]
    public void WhenRecorded_TheRunHoldsOpenReferencedIssuesThenTheIndex()
    {
        // Act
        StartingRun starting = RunSupplied(MixedContext());

        // Assert
        starting.SuppliedOpenIssueNumbers.ShouldBe([7, 3, 11, 12]);
    }

    [Fact]
    public void WhenAReferencedIssueIsAlsoInTheIndex_ItIsStoredOnce()
    {
        // Arrange
        TrackerContext trackerContext = new TrackerContextBuilder()
            .WithReferencedIssue(7, isOpen: true)
            .WithOpenIssueNumbers(11, 7, 12)
            .Build();

        // Act
        StartingRun starting = RunSupplied(trackerContext);

        // Assert
        starting.SuppliedOpenIssueNumbers.ShouldBe([7, 11, 12]);
    }

    [Fact]
    public void WhenAReferencedIssueIsClosed_ItIsLeftOut()
    {
        // Arrange
        TrackerContext trackerContext = new TrackerContextBuilder()
            .WithReferencedIssue(7, isOpen: false)
            .WithReferencedIssue(3, isOpen: true)
            .Build();

        // Act
        StartingRun starting = RunSupplied(trackerContext);

        // Assert
        starting.SuppliedOpenIssueNumbers.ShouldBe([3]);
    }

    [Fact]
    public void WhenAnIssueIsUnreadable_ItIsLeftOut()
    {
        // Arrange
        TrackerContext trackerContext = new TrackerContextBuilder()
            .WithReferencedIssue(3, isOpen: true)
            .WithUnreadableIssueNumbers([9])
            .Build();

        // Act
        StartingRun starting = RunSupplied(trackerContext);

        // Assert
        starting.SuppliedOpenIssueNumbers.ShouldBe([3]);
    }

    [Fact]
    public void WhenTheIndexIsUnreadable_OnlyTheOpenReferencedIssuesAreStored()
    {
        // Arrange
        TrackerContext trackerContext = new TrackerContextBuilder()
            .WithReferencedIssue(3, isOpen: true)
            .WithUnreadableIndex()
            .Build();

        // Act
        StartingRun starting = RunSupplied(trackerContext);

        // Assert
        starting.SuppliedOpenIssueNumbers.ShouldBe([3]);
    }

    [Fact]
    public void WhenTheContextIsEmpty_TheStoredSetIsEmptyNotNull()
    {
        // Act
        StartingRun starting = RunSupplied(new TrackerContextBuilder().Build());

        // Assert
        starting.SuppliedOpenIssueNumbers.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public void BeforeItIsRecorded_TheRunHasNoSuppliedSet()
    {
        // Act
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        // Assert
        starting.SuppliedOpenIssueNumbers.ShouldBeNull();
    }

    [Fact]
    public void WhenActivated_TheActiveRunCarriesTheSet()
    {
        // Act
        ActiveRun active = ActivatedRun();

        // Assert
        active.SuppliedOpenIssueNumbers.ShouldBe([7, 3, 11, 12]);
    }

    [Fact]
    public void WhenTheStartingRunFails_TheFailedRunCarriesTheSet()
    {
        // Act
        FailedRun failed = RunSupplied(MixedContext()).Fail(new FailureReason.ContainerError("start failed"));

        // Assert
        failed.SuppliedOpenIssueNumbers.ShouldBe([7, 3, 11, 12]);
    }

    [Fact]
    public void WhenTheActiveRunCompletes_TheCompletedRunCarriesTheSet()
    {
        // Act
        CompletedRun completed = ActivatedRun().Complete(0, BranchName.From("feat/1-x"), pullRequestUrl: null);

        // Assert
        completed.SuppliedOpenIssueNumbers.ShouldBe([7, 3, 11, 12]);
    }

    [Fact]
    public void WhenTheActiveRunFails_TheFailedRunCarriesTheSet()
    {
        // Act
        FailedRun failed = ActivatedRun().Fail(new FailureReason.NonZeroExit(1), BranchName.From("feat/1-x"));

        // Assert
        failed.SuppliedOpenIssueNumbers.ShouldBe([7, 3, 11, 12]);
    }
}
