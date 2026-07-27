using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.CompletedRunTests;

public sealed class RunResultSummaryTests
{
    private static ActiveRun CreateActiveRun()
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        return starting.Activate(ContainerId.From("container-123"), BranchName.From("feat/1-default"), MonitoredRepositoryId.New());
    }

    [Fact]
    public void WhenCompletedWithSummary_SummaryIsRetained()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        Foundry.Modules.Workers.Domain.ValueObjects.RunResultSummary summary = Foundry.Modules.Workers.Domain.ValueObjects.RunResultSummary.Create(
            resultText: "success",
            subtype: "final",
            isError: false,
            durationMs: 12345L,
            numTurns: 3,
            totalCostUsd: 0.05m,
            inputTokens: 1000,
            outputTokens: 500);

        // Act
        CompletedRun completed = active.Complete(0, null, null, summary);

        // Assert
        completed.ResultSummary.ShouldNotBeNull();
        completed.ResultSummary.ShouldSatisfyAllConditions(
            () => completed.ResultSummary!.ResultText.ShouldBe("success"),
            () => completed.ResultSummary!.Subtype.ShouldBe("final"),
            () => completed.ResultSummary!.IsError.ShouldBeFalse(),
            () => completed.ResultSummary!.DurationMs.ShouldBe(12345L),
            () => completed.ResultSummary!.NumTurns.ShouldBe(3),
            () => completed.ResultSummary!.TotalCostUsd.ShouldBe(0.05m),
            () => completed.ResultSummary!.InputTokens.ShouldBe(1000),
            () => completed.ResultSummary!.OutputTokens.ShouldBe(500));
    }

    [Fact]
    public void WhenCompletedWithNullSummary_SummaryIsNull()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();

        // Act
        CompletedRun completed = active.Complete(0, null, null, resultSummary: null);

        // Assert
        completed.ResultSummary.ShouldBeNull();
    }
}
