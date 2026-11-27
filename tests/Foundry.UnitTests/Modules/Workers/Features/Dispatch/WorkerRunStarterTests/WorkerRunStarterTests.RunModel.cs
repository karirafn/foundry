using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerRunStarterTests;

public sealed partial class HandleAsync
{
    private async Task<WorkerRun> DispatchedRunAsync()
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<WorkerRun>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenAnImplementRunIsRoutine_TheRunRecordsTheRoutineDemandModelAndEffort()
    {
        // Arrange
        await StoreTierModelsAndRoleModelsAsync();
        ClaimedRunPipeline sut = BuildHandler(orchestrator: new StubWorkerOrchestrator(succeeds: true));

        // Act
        await sut.HandleAsync(BuildEvent(demand: ImplementDemand.Routine), TestContext.Current.CancellationToken);

        // Assert
        WorkerRun run = await DispatchedRunAsync();
        (run.Demand, run.Model, run.Effort).ShouldBe((ImplementDemand.Routine, "sonnet", "low"));
    }

    [Fact]
    public async Task WhenAnImplementClaimCarriesNoDemand_TheRunRecordsDemanding()
    {
        // Arrange
        await StoreTierModelsAndRoleModelsAsync();
        ClaimedRunPipeline sut = BuildHandler(orchestrator: new StubWorkerOrchestrator(succeeds: true));

        // Act
        await sut.HandleAsync(BuildEvent(demand: null), TestContext.Current.CancellationToken);

        // Assert
        WorkerRun run = await DispatchedRunAsync();
        (run.Demand, run.Model, run.Effort).ShouldBe((ImplementDemand.Demanding, "opus", "xhigh"));
    }

    [Fact]
    public async Task WhenNoTierModelIsSet_TheRunRecordsAnEmptyModelAndEffort()
    {
        // Arrange
        ClaimedRunPipeline sut = BuildHandler(orchestrator: new StubWorkerOrchestrator(succeeds: true));

        // Act
        await sut.HandleAsync(BuildEvent(demand: ImplementDemand.Routine), TestContext.Current.CancellationToken);

        // Assert
        WorkerRun run = await DispatchedRunAsync();
        (run.Demand, run.Model, run.Effort).ShouldBe((ImplementDemand.Routine, string.Empty, string.Empty));
    }

    [Fact]
    public async Task WhenATriageRunIsDispatched_TheRunRecordsNoDemandAndTheTriageModelWithEmptyEffort()
    {
        // Arrange
        await StoreTierModelsAndRoleModelsAsync();
        ClaimedRunPipeline sut = BuildHandler(
            orchestrator: new StubWorkerOrchestrator(succeeds: true),
            roleCatalog: new StubWorkerRoleCatalogStore(CatalogWithRestricted(WorkerRole.Triage)));

        // Act
        await sut.HandleAsync(
            BuildEvent(context: new DispatchContext.Triage(), withoutBranch: true, demand: ImplementDemand.Routine),
            TestContext.Current.CancellationToken);

        // Assert
        WorkerRun run = await DispatchedRunAsync();
        (run.Demand, run.Model, run.Effort).ShouldBe(((ImplementDemand?)null, "triage-model", string.Empty));
    }

    [Fact]
    public async Task WhenAReviewRunIsDispatched_TheRunRecordsNoDemandAndTheReviewModelWithEmptyEffort()
    {
        // Arrange
        await StoreTierModelsAndRoleModelsAsync();
        ClaimedRunPipeline sut = BuildReviewHandler(orchestrator: new StubWorkerOrchestrator(succeeds: true));

        // Act
        await sut.HandleAsync(BuildReviewEvent(), TestContext.Current.CancellationToken);

        // Assert
        WorkerRun run = await DispatchedRunAsync();
        (run.Demand, run.Model, run.Effort).ShouldBe(((ImplementDemand?)null, "review-model", string.Empty));
    }

    [Fact]
    public async Task WhenTheRunFailsBeforeItsSpecIsBuilt_TheFailedRunStillRecordsItsDemandAndModel()
    {
        // Arrange
        await StoreTierModelsAndRoleModelsAsync();
        ClaimedRunPipeline sut = BuildHandler(
            issueContentQueries: new StubIssueContentQueries(
                Result<IssueContent>.Fail(new Error("GitHub.Unexpected", "boom"))));

        // Act
        await sut.HandleAsync(BuildEvent(demand: ImplementDemand.Routine), TestContext.Current.CancellationToken);

        // Assert
        WorkerRun run = await DispatchedRunAsync();
        run.ShouldBeOfType<FailedRun>();
        (run.Demand, run.Model, run.Effort).ShouldBe((ImplementDemand.Routine, "sonnet", "low"));
    }
}
