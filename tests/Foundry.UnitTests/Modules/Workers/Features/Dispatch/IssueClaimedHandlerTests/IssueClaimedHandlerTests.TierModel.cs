using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.IssueClaimedHandlerTests;

public sealed partial class HandleAsync
{
    private async Task StoreTierModelsAndRoleModelsAsync()
    {
        ImplementTierSettings routine = ImplementTierSettings.Create(ImplementDemand.Routine);
        routine.ChangeModel("sonnet");
        routine.ChangeEffort("low");
        ImplementTierSettings demanding = ImplementTierSettings.Create(ImplementDemand.Demanding);
        demanding.ChangeModel("opus");
        demanding.ChangeEffort("xhigh");
        WorkerRoleSettings triage = WorkerRoleSettings.Create(WorkerRole.Triage);
        triage.ChangeModel("triage-model");
        WorkerRoleSettings review = WorkerRoleSettings.Create(WorkerRole.Review);
        review.ChangeModel("review-model");
        _dbContext.AddRange(routine, demanding, triage, review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private static (string? Model, string? Effort) ManagedModelAndEffort(StubWorkerOrchestrator orchestrator)
    {
        using JsonDocument doc = JsonDocument.Parse(
            orchestrator.LastSpec.ShouldNotBeNull().EnvironmentVariables["CLAUDE_SETTINGS_JSON"]);

        return (
            doc.RootElement.TryGetProperty("model", out JsonElement model) ? model.GetString() : null,
            doc.RootElement.TryGetProperty("effortLevel", out JsonElement effort) ? effort.GetString() : null);
    }

    [Fact]
    public async Task WhenAnImplementRunIsRoutine_ItRunsWithTheRoutineTierModelAndEffort()
    {
        // Arrange
        await StoreTierModelsAndRoleModelsAsync();
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);

        // Act
        await sut.HandleAsync(BuildEvent(demand: ImplementDemand.Routine), TestContext.Current.CancellationToken);

        // Assert
        ManagedModelAndEffort(orchestrator).ShouldBe(("sonnet", "low"));
    }

    [Fact]
    public async Task WhenAnImplementRunIsDemanding_ItRunsWithTheDemandingTierModelAndEffort()
    {
        // Arrange
        await StoreTierModelsAndRoleModelsAsync();
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);

        // Act
        await sut.HandleAsync(BuildEvent(demand: ImplementDemand.Demanding), TestContext.Current.CancellationToken);

        // Assert
        ManagedModelAndEffort(orchestrator).ShouldBe(("opus", "xhigh"));
    }

    [Fact]
    public async Task WhenAnImplementClaimCarriesNoDemand_ItResolvesTheSameSettingAsDemanding()
    {
        // Arrange
        await StoreTierModelsAndRoleModelsAsync();
        StubWorkerOrchestrator withoutDemand = new(succeeds: true);
        StubWorkerOrchestrator demanding = new(succeeds: true);

        // Act
        await BuildHandler(orchestrator: withoutDemand)
            .HandleAsync(BuildEvent(demand: null), TestContext.Current.CancellationToken);
        await BuildHandler(orchestrator: demanding)
            .HandleAsync(BuildEvent(demand: ImplementDemand.Demanding), TestContext.Current.CancellationToken);

        // Assert
        ManagedModelAndEffort(withoutDemand).ShouldBe(ManagedModelAndEffort(demanding));
    }

    [Fact]
    public async Task WhenATriageRunIsDispatched_ItRunsWithTheTriageRoleModelAndNoEffort()
    {
        // Arrange
        await StoreTierModelsAndRoleModelsAsync();
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            roleCatalog: new StubWorkerRoleCatalogStore(CatalogWithRestricted(WorkerRole.Triage)));

        // Act
        await sut.HandleAsync(
            BuildEvent(context: new DispatchContext.Triage(), withoutBranch: true),
            TestContext.Current.CancellationToken);

        // Assert
        ManagedModelAndEffort(orchestrator).ShouldBe(("triage-model", null));
    }

    [Fact]
    public async Task WhenAReviewRunIsDispatched_ItRunsWithTheReviewRoleModelAndNoEffort()
    {
        // Arrange
        await StoreTierModelsAndRoleModelsAsync();
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        IssueClaimedHandler sut = BuildReviewHandler(orchestrator: orchestrator);

        // Act
        await sut.HandleAsync(BuildReviewEvent(), TestContext.Current.CancellationToken);

        // Assert
        ManagedModelAndEffort(orchestrator).ShouldBe(("review-model", null));
    }
}
