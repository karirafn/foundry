using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.FailedRunTests;

public sealed class RunResultSummaryTests
{
    private static ActiveRun CreateActiveRun()
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        return starting.Activate(ContainerId.From("container-123"), BranchName.From("feat/1-default"), MonitoredRepositoryId.New());
    }

    [Fact]
    public void WhenFailedWithSummary_SummaryIsRetained()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        Foundry.Modules.Workers.Domain.ValueObjects.RunResultSummary summary = Foundry.Modules.Workers.Domain.ValueObjects.RunResultSummary.Create(
            resultText: "error",
            subtype: "error",
            isError: true,
            durationMs: 9876L,
            numTurns: 1,
            totalCostUsd: 0.01m,
            inputTokens: 200,
            outputTokens: 50);

        // Act
        FailedRun failed = active.Fail(new FailureReason.NonZeroExit(1), branchNameOrNull: null, containerOutput: null, resultSummary: summary);

        // Assert
        failed.ResultSummary.ShouldNotBeNull();
        failed.ResultSummary.ShouldSatisfyAllConditions(
            () => failed.ResultSummary!.ResultText.ShouldBe("error"),
            () => failed.ResultSummary!.Subtype.ShouldBe("error"),
            () => failed.ResultSummary!.IsError.ShouldBeTrue(),
            () => failed.ResultSummary!.DurationMs.ShouldBe(9876L),
            () => failed.ResultSummary!.NumTurns.ShouldBe(1),
            () => failed.ResultSummary!.TotalCostUsd.ShouldBe(0.01m),
            () => failed.ResultSummary!.InputTokens.ShouldBe(200),
            () => failed.ResultSummary!.OutputTokens.ShouldBe(50));
    }

    [Fact]
    public void WhenFailedWithNullSummary_SummaryIsNull()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();

        // Act
        FailedRun failed = active.Fail(new FailureReason.NonZeroExit(1), branchNameOrNull: null, containerOutput: null, resultSummary: null);

        // Assert
        failed.ResultSummary.ShouldBeNull();
    }
}
