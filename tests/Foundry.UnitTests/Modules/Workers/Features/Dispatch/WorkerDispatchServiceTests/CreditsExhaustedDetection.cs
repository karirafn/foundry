using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

public sealed class CreditsExhaustedDetection : WorkerDispatchServiceTestBase
{
    // 429-only output — no terminal_reason, no parseable reset time → CreditsExhausted parse result
    private const string Credits429OnlyOutput =
        """
        Some prior output
        {"api_error_status":429}
        """;

    private WorkerDispatchService BuildServiceWithParser(
        string? containerLogs,
        WorkerStatus exitedStatus,
        IIntegrationEventDispatcher? integrationEventDispatcher = null)
    {
        IContainerOutputParser outputParser = new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance);
        ExitedWorkerOrchestrator orchestrator = new(exitedStatus, containerLogs);

        return base.BuildService(
            orchestrator,
            integrationEventDispatcher: integrationEventDispatcher,
            containerOutputParser: outputParser);
    }

    private void SeedGlobalSettings()
    {
        using FoundryDbContext db = CreateDbContext();
        GlobalSettings settings = GlobalSettings.Create();
        db.Set<GlobalSettings>().Add(settings);
        db.SaveChanges();
    }

    [Fact]
    public async Task WhenContainerExitsWithCreditsExhaustedOutput_TransitionsToFailedRunWithCreditsExhaustedReason()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-credits-exhausted-reason");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(Credits429OnlyOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.Reason.ShouldBeOfType<FailureReason.CreditsExhausted>();
    }

    [Fact]
    public async Task WhenContainerExitsWithCreditsExhaustedOutput_PublishesWorkerCreditsExhausted()
    {
        // Arrange
        SeedGlobalSettings();
        ActiveRun activeRun = SeedActiveRun("container-credits-exhausted-event");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            Credits429OnlyOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerCreditsExhausted creditsExhaustedEvent = dispatcher.Captured
            .OfType<WorkerCreditsExhausted>()
            .ShouldHaveSingleItem();
        creditsExhaustedEvent.ShouldSatisfyAllConditions(
            () => creditsExhaustedEvent.WorkerRunId.ShouldBe(activeRun.Id),
            () => creditsExhaustedEvent.IssueId.ShouldBe(activeRun.IssueId.Value));
    }

    [Fact]
    public async Task WhenContainerExitsWithCreditsExhaustedOutput_DoesNotSetUsageLimitResetsAt()
    {
        // Arrange — CreditsExhausted must not write UsageLimitResetsAt (that is UsageLimited territory)
        SeedGlobalSettings();
        SeedActiveRun("container-credits-exhausted-no-resets-at");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(Credits429OnlyOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? settings = await assertDb.Set<GlobalSettings>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        settings.ShouldNotBeNull();
        settings.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public async Task WhenContinuableFailureWithCreditsExhaustedReason_PublishesWorkerCreditsExhausted()
    {
        // Arrange — branch has commits so outcome is ContinuableFailure, not Failure
        SeedGlobalSettings();
        ActiveRun activeRun = SeedActiveRun("container-credits-continuable");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();

        IContainerOutputParser outputParser = new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance);
        ExitedWorkerOrchestrator orchestrator = new(exitedStatus, Credits429OnlyOutput);

        WorkerDispatchService sut = base.BuildService(
            orchestrator,
            integrationEventDispatcher: dispatcher,
            containerOutputParser: outputParser,
            postExitProviderQueries: new StubPostExitProviderQueries(hasCommits: true));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerCreditsExhausted creditsExhaustedEvent = dispatcher.Captured
            .OfType<WorkerCreditsExhausted>()
            .ShouldHaveSingleItem();
        creditsExhaustedEvent.WorkerRunId.ShouldBe(activeRun.Id);
        creditsExhaustedEvent.IssueId.ShouldBe(activeRun.IssueId.Value);
    }

    private sealed class ExitedWorkerOrchestrator(WorkerStatus exitedStatus, string? logs) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(
                new Error("Test.NoDispatch", "No dispatch in credits-exhausted tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.Available(exitedStatus));

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult(logs);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
