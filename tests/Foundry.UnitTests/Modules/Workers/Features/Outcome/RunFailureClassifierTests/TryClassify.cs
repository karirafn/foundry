using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.RunFailureClassifierTests;

public sealed class TryClassify
{
    private static ActiveRun ActiveRun(WorkerRole role, RunModel? runModel) =>
        StartingRun.Begin(IssueId.New(), WorkerRunId.New(), role, runModel: runModel)
            .Activate(ContainerId.From("container-1"), BranchName.From("feat/1-x"), MonitoredRepositoryId.New());

    [Fact]
    public void WhenARoutineImplementRunsModelIsUnavailable_TheReasonNamesTheRoutineModelSetting()
    {
        // Arrange
        ActiveRun run = ActiveRun(WorkerRole.Implement, new RunModel(ImplementDemand.Routine, "sonnet", string.Empty));

        // Act
        FailureReason? reason = RunFailureClassifier.TryClassify(new ContainerOutputParseResult.ModelUnavailable(), run);

        // Assert
        reason.ShouldNotBeNull().Summary.ShouldContain("'Routine model' setting");
    }

    [Fact]
    public void WhenADemandingImplementRunsModelIsUnavailable_TheReasonNamesTheDemandingModelSetting()
    {
        // Arrange
        ActiveRun run = ActiveRun(WorkerRole.Implement, new RunModel(ImplementDemand.Demanding, "opus", string.Empty));

        // Act
        FailureReason? reason = RunFailureClassifier.TryClassify(new ContainerOutputParseResult.ModelUnavailable(), run);

        // Assert
        reason.ShouldNotBeNull().Summary.ShouldContain("'Demanding model' setting");
    }

    [Fact]
    public void WhenAnImplementRunRecordedNoDemand_TheReasonNamesTheDemandingModelSetting()
    {
        // Arrange
        ActiveRun run = ActiveRun(WorkerRole.Implement, runModel: null);

        // Act
        FailureReason? reason = RunFailureClassifier.TryClassify(new ContainerOutputParseResult.ModelUnavailable(), run);

        // Assert
        reason.ShouldBe(new FailureReason.ModelUnavailable("implement", ImplementDemand.Demanding));
    }

    [Theory]
    [InlineData("triage")]
    [InlineData("review")]
    public void WhenATriageOrReviewRunsModelIsUnavailable_TheReasonNamesTheRole(string roleName)
    {
        // Arrange
        WorkerRole role = WorkerRole.All.Single(candidate => candidate.Name == roleName);
        ActiveRun run = ActiveRun(role, new RunModel(demand: null, "opus", string.Empty));

        // Act
        FailureReason? reason = RunFailureClassifier.TryClassify(new ContainerOutputParseResult.ModelUnavailable(), run);

        // Assert
        reason.ShouldBe(new FailureReason.ModelUnavailable(roleName));
        reason!.Summary.ShouldContain($"'{roleName}' worker role's model setting");
    }
}
