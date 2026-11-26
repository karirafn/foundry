using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.StartingRunTests;

public sealed class RecordsRunModel
{
    [Fact]
    public void WhenATriageRunIsBegunWithADemand_ItIsRefused()
    {
        // Act
        Action begin = () => StartingRun.Begin(
            IssueId.New(),
            WorkerRunId.New(),
            WorkerRole.Triage,
            runModel: new RunModel(ImplementDemand.Routine, "sonnet", string.Empty));

        // Assert
        begin.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void WhenTheRunFails_TheFailedRunKeepsItsDemandModelAndEffort()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(
            IssueId.New(),
            WorkerRunId.New(),
            WorkerRole.Implement,
            runModel: new RunModel(ImplementDemand.Routine, "sonnet", "low"));

        // Act
        FailedRun failed = starting.Fail(new FailureReason.ContainerError("image not found"));

        // Assert
        (failed.Demand, failed.Model, failed.Effort).ShouldBe((ImplementDemand.Routine, "sonnet", "low"));
    }

    [Fact]
    public void WhenBegunWithoutARunModel_TheRunRecordsNone()
    {
        // Act
        StartingRun run = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        // Assert
        (run.Demand, run.Model, run.Effort).ShouldBe(((ImplementDemand?)null, (string?)null, (string?)null));
    }
}
