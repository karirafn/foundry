using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.StartingRunTests;

public sealed class RecordPreparedBase
{
    private static readonly RunBase Prepared = new(
        CommitSha.From("1111111111111111111111111111111111111111"),
        CommitSha.From("2222222222222222222222222222222222222222"));

    private static StartingRun PreparedRun()
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        starting.RecordPreparedBase(Prepared);
        return starting;
    }

    private static ActiveRun ActivatedRun() =>
        PreparedRun().Activate(ContainerId.From("container-1"), BranchName.From("feat/1-x"), MonitoredRepositoryId.New());

    [Fact]
    public void WhenRecorded_TheRunHasThatBase()
    {
        // Act
        StartingRun starting = PreparedRun();

        // Assert
        starting.Base.ShouldBe(Prepared);
    }

    [Fact]
    public void BeforeItIsRecorded_TheRunHasNoBase()
    {
        // Act
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        // Assert
        starting.Base.ShouldBeNull();
    }

    [Fact]
    public void WhenActivated_TheActiveRunCarriesTheBase()
    {
        // Act
        ActiveRun active = ActivatedRun();

        // Assert
        active.Base.ShouldBe(Prepared);
    }

    [Fact]
    public void WhenTheStartingRunFails_TheFailedRunCarriesTheBase()
    {
        // Act
        FailedRun failed = PreparedRun().Fail(new FailureReason.ContainerError("start failed"));

        // Assert
        failed.Base.ShouldBe(Prepared);
    }

    [Fact]
    public void WhenTheActiveRunCompletes_TheCompletedRunCarriesTheBase()
    {
        // Act
        CompletedRun completed = ActivatedRun().Complete(0, BranchName.From("feat/1-x"), pullRequestUrl: null);

        // Assert
        completed.Base.ShouldBe(Prepared);
    }

    [Fact]
    public void WhenTheActiveRunFails_TheFailedRunCarriesTheBase()
    {
        // Act
        FailedRun failed = ActivatedRun().Fail(new FailureReason.NonZeroExit(1), BranchName.From("feat/1-x"));

        // Assert
        failed.Base.ShouldBe(Prepared);
    }
}
